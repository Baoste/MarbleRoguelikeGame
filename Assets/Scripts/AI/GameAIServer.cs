using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MarblesECS.PhysX;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarblesECS.AI
{
    /// <summary>Opt-in local JSON-lines transport. Socket work never calls Unity APIs.</summary>
    public sealed class GameAIServer : MonoBehaviour
    {
        private sealed class Pending
        {
            internal string line;
            internal readonly TaskCompletionSource<string> answer = new TaskCompletionSource<string>();
        }
        private readonly ConcurrentQueue<Pending> requests = new ConcurrentQueue<Pending>();
        private TcpListener listener;
        private TcpClient client;
        private bool busy, stopping, searchWorker;
        private uint seed = 1;
        private string scenePath;
        private GameAIEnvironment environment;
        private GameAIConfig config = new GameAIConfig();
        private string sceneError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--game-ai-port");
            if (index < 0) return;
            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int port) || port < 1024 || port > 65535)
                throw new ArgumentException("--game-ai-port requires a port from 1024 to 65535.");
            var host = new GameObject("Game AI Server").AddComponent<GameAIServer>();
            DontDestroyOnLoad(host.gameObject);
            host.searchWorker = Array.IndexOf(args, "--game-ai-search-worker") >= 0;
            host.StartServer(port);
        }

        private void StartServer(int port)
        {
            MRes.MResManualSimulation.Enabled = true;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            SceneManager.sceneLoaded += PrepareScene;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            _ = Serve();
        }

        private async Task Serve()
        {
            while (!stopping)
            {
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        string line;
                        while (!stopping && (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (line.Length > 16384) throw new IOException("Request too large.");
                            var pending = new Pending { line = line };
                            requests.Enqueue(pending);
                            string answer = await pending.answer.Task.ConfigureAwait(false);
                            await writer.WriteLineAsync(answer).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception) { if (stopping) return; }
            }
        }

        private void PrepareScene(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex < 0) return;
            scenePath = scene.path;
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var controller in root.GetComponentsInChildren<MarbleGameController>(true))
                    {
                        controller.AutomationControlled = true; controller.AutoStart = true;
                        var balance = GameBalanceLoader.Load(controller.BalanceJson);
                        balance.Marble.RandomSeed = seed == 0 ? 1u : seed;
                        controller.ConfigureCampaign(balance);
                    }
                    // UI is not part of game authority. DevicePlacementUI is driven explicitly below.
                    foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (behaviour is DevicePlacementUI || behaviour is UnityEngine.EventSystems.EventSystem ||
                            (behaviour != null && (behaviour.GetType().Namespace == "MarblesECS.Presentation" ||
                            behaviour.GetType().Name.EndsWith("UI") || behaviour.GetType().Name == "ContentDrugPanel")))
                            behaviour.enabled = false;
                }
                UnityEngine.Random.InitState(unchecked((int)seed));
            }
            catch (Exception e) { sceneError = e.ToString(); }
        }

        private void Update()
        {
            if (!busy && requests.TryDequeue(out var request)) StartCoroutine(Handle(request));
        }

        private IEnumerator Handle(Pending pending)
        {
            busy = true;
            GameAIRequest request = null;
            GameAIResponse response = null;
            try { request = JsonUtility.FromJson<GameAIRequest>(pending.line); }
            catch (Exception e) { response = new GameAIResponse { error = e.ToString() }; }
            if (request != null && request.command == "reset")
            {
                try
                {
                    config = request.config ?? new GameAIConfig(); config.Validate();
                    seed = request.seed == 0 ? 1u : request.seed;
                    sceneError = null; environment = null;
                    if (string.IsNullOrEmpty(scenePath)) scenePath = SceneManager.GetActiveScene().path;
                }
                catch (Exception e) { response = new GameAIResponse { error = e.ToString() }; }
                if (response == null)
                {
                    AsyncOperation load = null;
                    try { load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single); }
                    catch (Exception e) { response = new GameAIResponse { error = e.ToString() }; }
                    if (load != null)
                    {
                        while (!load.isDone) yield return null;
                        yield return null; // allow controller Start and destruction of old views to complete
                        try
                        {
                            if (sceneError != null) throw new InvalidOperationException(sceneError);
                            var controller = FindObjectOfType<MarbleGameController>();
                            var placement = FindObjectOfType<DevicePlacementUI>(true);
                            environment = new GameAIEnvironment(controller, placement, config);
                            response = environment.Observe();
                        }
                        catch (Exception e) { response = new GameAIResponse { error = e.ToString() }; }
                    }
                }
            }
            else if (request != null)
            {
                try
                {
                    if (request.command == "hello") response = new GameAIResponse { reason = "game-ai-v1", unityVersion = Application.unityVersion };
                    else if (environment == null) throw new InvalidOperationException("Reset the environment first.");
                    else if (request.command == "observe") response = environment.Observe();
                    else if (request.command == "step") response = environment.Step(request.actionKey);
                    else if (request.command == "sample_future")
                    {
                        if (!searchWorker) throw new InvalidOperationException("Future randomization is restricted to search workers.");
                        FindObjectOfType<MarbleGameController>().SampleAutomationFuture(request.seed);
                        response = environment.Observe();
                    }
                    else throw new ArgumentException("Unknown command: " + request.command);
                }
                catch (Exception e) { response = new GameAIResponse { error = e.ToString() }; }
            }
            response = response ?? new GameAIResponse { error = "Invalid request or scene load failure." };
            response.requestId = request?.requestId ?? 0;
            pending.answer.TrySetResult(JsonUtility.ToJson(response));
            busy = false;
        }

        private void OnDestroy()
        {
            stopping = true;
            SceneManager.sceneLoaded -= PrepareScene;
            listener?.Stop(); client?.Close();
            while (requests.TryDequeue(out var pending)) pending.answer.TrySetCanceled();
            MRes.MResManualSimulation.Enabled = false;
        }
    }
}
