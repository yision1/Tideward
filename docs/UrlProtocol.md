# URL Protocol

Other software and websites can use the URL protocol `tideward` to call some features of Tideward. The URL protocol is registered only when the user enables this feature on the advanced settings page.

![URL Protocol](https://github.com/user-attachments/assets/6ad69c51-fb1b-4de2-9569-f33530a5390a)

## Available features

The parameter `game_biz` below is the game region identifier and can be viewed in [GameBiz.cs](https://github.com/yision1/Tideward/blob/main/src/Tideward.Core/GameBiz.cs).

| game_biz (string) | Description                     |
| ----------------- | ------------------------------- |
| wuwa_cn           | Wuthering Waves (Mainland China) |
| wuwa_global       | Wuthering Waves (Global)         |

### Start game

```
tideward://startgame/{game_biz}?install_path={install_path}
```

**Acceptable query arguments**

|Key|Type|Description|
|---|---|---|
|install_path| `string` (Option) | Folder full path of game executable. |

### Record playtime

```
tideward://playtime/{game_biz}?pid={pid}
```

**Acceptable query arguments**

|Key|Type|Description|
|---|---|---|
|pid| `int` (Option) | Game process id. |
