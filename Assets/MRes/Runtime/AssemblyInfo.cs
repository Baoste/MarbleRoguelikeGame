using System.Runtime.CompilerServices;

// Gameplay content can reuse the package runtime's bounded JSON reader.
[assembly: InternalsVisibleTo("MarblesECS.Runtime")]
