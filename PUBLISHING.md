# Alternative Weapons Control: сборка, проверка и выкладка в Steam Workshop

> Preparing a release (version, change lists, README, translations, build, commit rules)? Read the Sea Power project doc `claude/release-howto.md` first (also: `claude/steam-workshop.md` for the Workshop ids and how to check what is published).

## 1. Структура проекта

```
AlternativeWeaponsControl/
├─ AlternativeWeaponsControl.csproj  <- сборка; после Build копирует dll в Release
├─ nuget.config                      <- пустой список источников NuGet (сборка без сети)
├─ README.BBCode.txt                 <- описание для страницы Steam Workshop (BBCode)
├─ README.txt                        <- то же простым текстом (форумы)
├─ README.md                         <- то же в Markdown (GitHub)
├─ src/
│  ├─ AwcPlugin.cs                   <- сам плагин (BepInEx BaseUnityPlugin): настройки, хуки, вкладка в Unit Status
│  ├─ TabHeader.cs                   <- кнопка вкладки: (i) с легендой и шестерёнка настроек
│  ├─ SettingsWindow.cs              <- окно настроек в игре
│  ├─ WeaponsModel.cs                <- пусковые, домены целей, переключатели, Standing Orders, сохранение
│  ├─ WeaponsAi.cs                   <- автоприменение в AI и CIWS, цель отдельной пусковой, каналы управления огнём
│  ├─ WeaponsView.cs                 <- интерфейс вкладки (Noesis)
│  └─ AnchorChainEntry.cs            <- точка входа Anchor Chain + проверка галочки в меню Mods
└─ Release/                          <- ЭТО и есть мод: содержимое папки выкладываете
   ├─ _info.ini
   ├─ AlternativeWeaponsControl.dll
   └─ thumbnail.jpg                  <- картинка для Workshop (JPEG 512x512, < 1 МБ)
```

В StreamingAssets папка мода должна называться **`AlternativeWeaponsControl`** (не `Release`): по имени папки игра хранит
галочку мода в `usersettings.ini`, и по нему же мод проверяет, включён ли он в меню **Mods**.
Чужие dll (AnchorChain, BepInEx, 0Harmony) в папку мода не кладите.

## 2. Сборка

```
dotnet build -c Release -p:GameDir="D:\Steam\steamapps\common\Sea Power"
```

AnchorChain.dll берётся из `<Steam>\steamapps\workshop\content\1286220\3380210757\AnchorChain.dll`
(иначе `-p:AnchorChainDll="..."`). Результат копируется в `Release\AlternativeWeaponsControl.dll`.

## 3. Локальная проверка

1. Скопируйте содержимое `Release` в `Sea Power\Sea Power_Data\StreamingAssets\AlternativeWeaponsControl\`.
2. Включите мод в меню **Mods** (и Anchor Chain), Apply, **полностью перезапустите игру**.
3. В `BepInEx\LogOutput.log` должно быть:
   * `Alternative Weapons Control 1.0.0 (build ...) loaded; follows its tick in the game's Mods menu.`
   * `Enabled in the game's Mods menu (AlternativeWeaponsControl).`
   * без предупреждений `... not found (game updated?)`.
4. В миссии: Unit Status своего корабля → вторая вкладка **Weapons**; на её кнопке (i) — легенда, шестерёнка — окно
   настроек (`Sea Power_Data\StreamingAssets\user\AlternativeWeaponsControl\AlternativeWeaponsControl.cfg`).
5. Снимите галочку в Mods и зайдите в миссию: `Disabled in the game's Mods menu ...`, вкладки Weapons нет.
6. Для диагностики — Debug в окне настроек: `BepInEx\AlternativeWeaponsControl.trace.txt`.

## 4. Выкладка

Workshop-аплоадер игры, папка мода = содержимое `Release` (_info.ini, AlternativeWeaponsControl.dll, thumbnail.jpg).
Короткое описание (en/ru) — в `Release/_info.ini`; описание страницы Workshop — `README.BBCode.txt`.
thumbnail.jpg сейчас временная (сгенерирована) — замените своей.

## 5. Версия

Константа `PluginVersion` в `src/AwcPlugin.cs` (BepInPlugin, ACPlugin, лог) и `<Version>` в csproj — менять обе.
