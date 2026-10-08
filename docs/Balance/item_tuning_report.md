# Item & Medicine Effect Tuning

经济模拟：4 配置 × 3 策略 × 10,000 Runs；固定 Seed `20260930`。阶段目标、奖励、物理分布和正式价格保持不变，仅比较效果强度与必要的单项价格修正。

## 护栏

- 已建模内容的平均 Pick Rate 不应由单个选项长期垄断；90% 为支配策略红线，75% 为观察线。
- 降低最高 EV 集群时，同时保留至少三种可购买的 Rush 路线，避免把唯一强解换成唯一弱解。
- 路线/物理型装置和药物缺少变体物理样本，除修复无限递归外不凭启发式 EV 大幅改强度。
- 胜率只作回归护栏；效果方案不会为了追一个固定胜率而反向抬高阶段目标。

## Current

| KPI | Conservative | Balanced | Aggressive |
| --- | ---: | ---: | ---: |
| Run Win Rate | 94.8% | 99.2% | 95.9% |
| Round Clear Rate | 99.1% | 99.8% | 98.8% |
| Round Failure Rate | 0.9% | 0.2% | 1.2% |
| Cashout Rate | 97.3% | 98.9% | 97.7% |
| Dead Run Rate | 5.0% | 0.7% | 2.7% |
| 平均 Run 时长（秒） | 51.3 | 40.3 | 24.5 |
| 平均发射弹珠 | 140.1 | 114.2 | 64.7 |
| 平均剩余血量 | 48.2 | 43.9 | 23.5 |
| 平均得分 | 3399.0 | 3794.4 | 4010.1 |
| 平均金币收入 | 245.8 | 247.6 | 222.2 |
| 平均金币支出 | 67.8 | 62.2 | 130.1 |
| 平均金币存量 | 208.0 | 215.4 | 122.1 |
| 平均购买数 | 3.90 | 3.38 | 5.61 |
| 平均刷新数 | 0.00 | 0.81 | 0.00 |
| 平均 Device 数 | 1.30 | 1.31 | 3.71 |
| 平均 Medicine 数 | 1.54 | 1.47 | 0.03 |
| 平均 Special Ball 数 | N/A | N/A | N/A |
| Score Variance | 56,215.9 | 182,537.1 | 635,865.1 |
| Gold Variance | 720.7 | 385.5 | 1,539.6 |
| Run Variance（时长秒²） | 35.3 | 103.8 | 28.6 |

- 最高单策略 Pick Rate：延长药剂 / Conservative = 89.4%。
- 已建模且平均 Pick Rate ≥5%：6/11。
- 最高/最低建模 EV÷价格：Multi Ding 0.0239 / 增幅塔 0.0027。

## ItemConservative

| KPI | Conservative | Balanced | Aggressive |
| --- | ---: | ---: | ---: |
| Run Win Rate | 96.4% | 99.2% | 96.1% |
| Round Clear Rate | 99.4% | 99.8% | 98.9% |
| Round Failure Rate | 0.6% | 0.2% | 1.1% |
| Cashout Rate | 98.1% | 98.9% | 97.9% |
| Dead Run Rate | 3.4% | 0.7% | 2.6% |
| 平均 Run 时长（秒） | 48.5 | 39.5 | 23.8 |
| 平均发射弹珠 | 130.9 | 111.9 | 62.8 |
| 平均剩余血量 | 51.3 | 44.3 | 24.2 |
| 平均得分 | 3452.0 | 3812.3 | 4070.5 |
| 平均金币收入 | 253.8 | 248.3 | 224.2 |
| 平均金币支出 | 101.4 | 60.8 | 129.1 |
| 平均金币存量 | 182.4 | 217.5 | 125.2 |
| 平均购买数 | 5.40 | 3.30 | 5.48 |
| 平均刷新数 | 0.00 | 0.78 | 0.00 |
| 平均 Device 数 | 1.28 | 1.32 | 3.66 |
| 平均 Medicine 数 | 1.58 | 1.45 | 0.03 |
| 平均 Special Ball 数 | N/A | N/A | N/A |
| Score Variance | 59,245.2 | 206,994.8 | 717,133.4 |
| Gold Variance | 501.3 | 387.2 | 1,561.9 |
| Run Variance（时长秒²） | 35.6 | 103.3 | 27.2 |

- 最高单策略 Pick Rate：肾上腺素 / Conservative = 88.8%。
- 已建模且平均 Pick Rate ≥5%：6/11。
- 最高/最低建模 EV÷价格：Multi Ding 0.0253 / 增幅塔 0.0027。

效果覆盖：

```json
{
  "Devices": {
    "104": {
      "Value": 0.7
    },
    "107": {
      "Value": 1.9
    },
    "108": {
      "Value": 2.8
    },
    "113": {
      "ScoreMultiplier": 1.9
    },
    "114": {
      "RushChanceAdd": 0.11
    }
  },
  "Drugs": {
    "100": {
      "Price": 21,
      "RushChanceAdd": 0.23
    },
    "101": {
      "Price": 22,
      "RushScoreMultiplier": 1.7
    },
    "102": {
      "Price": 12
    },
    "107": {
      "DurationSeconds": 8.0,
      "Stackable": false
    }
  },
  "DrugEffects": {
    "102": {
      "22": 1.45
    }
  }
}
```

## ItemRecommended

| KPI | Conservative | Balanced | Aggressive |
| --- | ---: | ---: | ---: |
| Run Win Rate | 94.8% | 99.2% | 95.9% |
| Round Clear Rate | 99.1% | 99.8% | 98.8% |
| Round Failure Rate | 0.9% | 0.2% | 1.2% |
| Cashout Rate | 97.3% | 98.9% | 97.7% |
| Dead Run Rate | 5.0% | 0.7% | 2.7% |
| 平均 Run 时长（秒） | 51.3 | 40.3 | 24.5 |
| 平均发射弹珠 | 140.1 | 114.2 | 64.7 |
| 平均剩余血量 | 48.2 | 43.9 | 23.5 |
| 平均得分 | 3399.0 | 3794.4 | 4010.1 |
| 平均金币收入 | 245.8 | 247.6 | 222.2 |
| 平均金币支出 | 67.8 | 62.2 | 130.1 |
| 平均金币存量 | 208.0 | 215.4 | 122.1 |
| 平均购买数 | 3.90 | 3.38 | 5.61 |
| 平均刷新数 | 0.00 | 0.81 | 0.00 |
| 平均 Device 数 | 1.30 | 1.31 | 3.71 |
| 平均 Medicine 数 | 1.54 | 1.47 | 0.03 |
| 平均 Special Ball 数 | N/A | N/A | N/A |
| Score Variance | 56,215.9 | 182,537.1 | 635,865.1 |
| Gold Variance | 720.7 | 385.5 | 1,539.6 |
| Run Variance（时长秒²） | 35.3 | 103.8 | 28.6 |

- 最高单策略 Pick Rate：延长药剂 / Conservative = 89.4%。
- 已建模且平均 Pick Rate ≥5%：6/11。
- 最高/最低建模 EV÷价格：Multi Ding 0.0239 / 增幅塔 0.0027。

效果覆盖：

```json
{
  "Devices": {
    "104": {
      "Value": 0.65
    },
    "107": {
      "Value": 1.85
    },
    "108": {
      "Value": 2.7
    },
    "113": {
      "ScoreMultiplier": 1.85
    },
    "114": {
      "RushChanceAdd": 0.11
    }
  },
  "Drugs": {
    "100": {
      "Price": 21,
      "RushChanceAdd": 0.23
    },
    "101": {
      "Price": 22,
      "RushScoreMultiplier": 1.68
    },
    "102": {
      "Price": 12
    },
    "107": {
      "DurationSeconds": 6.0,
      "Stackable": false
    }
  },
  "DrugEffects": {
    "102": {
      "22": 1.45
    }
  }
}
```

## ItemExperimental

| KPI | Conservative | Balanced | Aggressive |
| --- | ---: | ---: | ---: |
| Run Win Rate | 90.9% | 97.6% | 95.8% |
| Round Clear Rate | 98.4% | 99.5% | 98.9% |
| Round Failure Rate | 1.6% | 0.5% | 1.1% |
| Cashout Rate | 96.1% | 97.5% | 96.8% |
| Dead Run Rate | 8.7% | 2.2% | 2.2% |
| 平均 Run 时长（秒） | 54.5 | 46.6 | 28.1 |
| 平均发射弹珠 | 150.3 | 132.8 | 75.0 |
| 平均剩余血量 | 45.7 | 39.1 | 19.7 |
| 平均得分 | 3342.5 | 3622.1 | 3798.6 |
| 平均金币收入 | 237.7 | 235.9 | 211.9 |
| 平均金币支出 | 75.9 | 52.3 | 126.4 |
| 平均金币存量 | 191.8 | 213.6 | 115.6 |
| 平均购买数 | 5.79 | 4.24 | 6.42 |
| 平均刷新数 | 0.00 | 0.00 | 0.00 |
| 平均 Device 数 | 1.31 | 1.00 | 4.08 |
| 平均 Medicine 数 | 1.63 | 2.44 | 0.02 |
| 平均 Special Ball 数 | N/A | N/A | N/A |
| Score Variance | 95,115.6 | 83,114.4 | 349,028.2 |
| Gold Variance | 670.0 | 379.0 | 1,128.8 |
| Run Variance（时长秒²） | 36.2 | 98.8 | 28.7 |

- 最高单策略 Pick Rate：肾上腺素 / Conservative = 90.3%。
- 已建模且平均 Pick Rate ≥5%：6/11。
- 最高/最低建模 EV÷价格：肾上腺素 0.0253 / 增幅塔 0.0034。

效果覆盖：

```json
{
  "Devices": {
    "101": {
      "Value": 1.25
    },
    "104": {
      "Value": 0.5
    },
    "107": {
      "Value": 1.55
    },
    "108": {
      "Value": 2.15
    },
    "113": {
      "ScoreMultiplier": 1.6
    },
    "114": {
      "RushChanceAdd": 0.08
    }
  },
  "Drugs": {
    "100": {
      "Price": 15,
      "RushChanceAdd": 0.18
    },
    "101": {
      "Price": 15,
      "RushScoreMultiplier": 1.5
    },
    "102": {
      "Price": 7
    },
    "107": {
      "DurationSeconds": 4.0,
      "Stackable": false
    }
  },
  "DrugEffects": {
    "102": {
      "22": 1.3
    }
  }
}
```

## 采用结论

正式采用 `ItemRecommended`：压低当前购买率最高的三种 Rush 药物与乘法装置，保留各自定位；四叶草与肾上腺素不再组成 100% 确定点火。

`无限增值` 无法仅靠价格平衡：复制体继承复制能力会产生 2^n 球数。运行时改为复用 `MaxSplitGeneration` 的复制代数上限，默认最多复制 2 代；这是本轮唯一必要的规则级安全修复。

原始逐 Run 结果见 `item_tuning_results.csv`，效果 EV 与 Pick Rate 见 `item_tuning_ev.csv`，聚合结果见 `item_tuning_summary.json`。
