# Alternative Weapons Control — заметки для продолжения работы (для Claude)

Мод для Sea Power: вкладка Weapons в окне Unit Status (своих кораблей и подводных лодок) — пусковые, цели, боезапас,
каналы управления огнём, автоматическое применение по пусковым и доменам целей, огонь / прекращение / перезарядка;
поправки к управлению огнём орудий. BepInEx 5 + Harmony, загружается через Anchor Chain (Workshop 3380210757). Устроен как
Alternative Damage Model: настройки — шестерёнка на кнопке вкладки, легенда — (i) рядом. Выделен из Smart Windows.
Доступ к игре — через рефлексию (класс `R` в WeaponsModel.cs: поля/свойства любой видимости, методы по имени с подбором
перегрузки, кэш); Noesis — напрямую (ссылка на Noesis.NoesisGUI в csproj).

## Сборка в песочнице
1. `apt-get update && apt-get install -y dotnet-sdk-8.0`.
2. `add_repo` (владелец rgreat, приватные): SeaPower.AlternativeWeaponsControl, SeaPower.GameFiles (только чтение);
   клоны в `/home/claude/<имя в нижнем регистре>`, по одному, таймаут ~10 мин.
3. `/home/claude/game/Managed` ← `SeaPower.GameFiles/Managed/*.dll`;
   `/home/claude/game/bepinex` ← https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip;
   `/home/claude/game/ac` ← https://github.com/SeaPower-Modders/AnchorChain/releases/download/V1.1.0/dev.zip.
   Пустой `nuget.config` в репозитории обязателен (nuget.org закрыт).
4. Скрипт `/home/claude/tools/build_awc.sh [repoDir] [zip]` (в репозитории его нет — создать заново):
   `dotnet build -c Release -p:ManagedDir=/home/claude/game/Managed -p:BepInExCore=/home/claude/game/bepinex/BepInEx/core
   -p:AnchorChainDll=/home/claude/game/ac/AnchorChain.dll`, затем zip БЕЗ корневой папки (Release/, src/, csproj,
   nuget.config, README*, PUBLISHING.md, DEVNOTES.md) → `/home/claude/out/AlternativeWeaponsControl.zip`.
Сборка сама копирует dll в `Release/` (target StageRelease). Номер билда (`BuildStamp`, UTC `yyyyMMdd.HHmm`) пишется в лог.

## Предпочтения пользователя
Общение — по-русски, коротко; в .cfg — один переключатель на функцию. В отчётах не повторять рутинное. Править исходники
и коммитить — только по просьбе пользователя. Если правка ломает работу мода на поддерживаемых версиях игры —
предупредить заранее.

## Совместимость
`_info.ini` ApproximateVersion=0.8.6 (члены игры проверены по декомпиляции 0.8.4 и 0.8.5; 0.8.6 - по описанию, не по коду).
Отсутствующее имя → мягкий отказ функции (предупреждение в логе).

## Структура
* `AwcPlugin.cs` — плагин: .cfg (`Sea Power_Data/StreamingAssets/user/AlternativeWeaponsControl/AlternativeWeaponsControl.cfg`,
  как у ADM; правка файла вне игры подхватывается через 0,5 с после записи), галочка в меню Mods (`ModState`,
  LoadScreen / FileManager.SaveDirectories), установка хуков, вкладка в UnitStatusViewModel и её показ.
* `TabHeader.cs` — кнопка вкладки: «Weapons» + (i) с легендой (подсказка, `AwcPlugin.Legend()` в WeaponsView.cs) +
  шестерёнка (окно настроек). Кнопки вкладок — RadioButton с DataContext = UnitStatusTabViewModel; наша — где DataContext
  = наша вкладка; в её TextBlock — Run(текст) + InlineUIContainer(иконки), как ADM на «Damage Control». Поиск раз в 0,5 с,
  пока не найдена; игра переписала текст — рисуется заново.
* `SettingsWindow.cs` — окно настроек в стиле ADM без профилей: группы Weapons tab / Fire control / Diagnostics, справка
  под мышью, точка «изменено, не применено» (отличие от сохранённого, как у ADM), ↺ — к умолчанию, Cancel / Apply / OK (Cancel — вернуть сохранённое и закрыть); работает на копии, Apply пишет .cfg. Размер в единицах UI 820×580 (всё видно сразу;
  не больше экрана − 40 px), ресайз за края и углы как у ADM (560..1600 × 420..1400), размер держится до выхода из игры.
* `AnchorChainEntry.cs` — точка входа Anchor Chain + чтение галочки мода (как у ADM).
* `WeaponsModel.cs` (пусковые, домены, переключатели, Standing Orders, сохранение), `WeaponsAi.cs` (автоприменение,
  цель отдельной пусковой, каналы), `WeaponsView.cs` (интерфейс вкладки).
* Окна UnitStatusView — postfix конструктора (список слабых ссылок); каждый кадр (Update, при включённом моде) для видимых:
  кнопка вкладки и хост содержимого (`HostStep`): первый элемент с Visibility, привязанной к «…Tab.IsSelected», его
  Panel — туда Border (та же ячейка, Margin), Child = наш вид, виден, пока вкладка выбрана; `WeaponsTick` там же.
* Вместе со Smart Windows: её вкладка Reference — kind 5, наша — 6; хуки на одни и те же методы UnitStatusViewModel и свои
  хосты не мешают друг другу. Ссылки вкладки на установки открывают Unit Reference без дерева — Smart Windows (1.5.1+)
  считает такие окна окнами Weapon Reference.
* Выключен в меню Mods: вкладка недоступна (после смены юнита), хуки AI/огня ничего не меняют (`Enabled`).

## Настройки (.cfg)
[UI] ClickVolume (0.4; 0 — без звука); [FireControl] GunOneDirector (true), GunChannelLimit (false); [General] Debug
(trace в `BepInEx/AlternativeWeaponsControl.trace.txt`).

## Модель (`WeaponsModel.cs`)
* Вкладка (kind 6) вставляется в `Tabs` второй. Колонка имён — WEAPONS; подзаголовок раздела — счёт по видам
  (`Noun`: Gun — turret, CIWS и Torpedo — mount, прочие — launcher: «2 launchers, 2 mounts»). Доступна для своих Vessel / Submarine с пусковыми, пока мод включён.
* Пусковые = `_obp._weaponSystems` (индекс в списке — ключ настроек) без Chaff / Noisemaker / Hardpoint / UNKNOWN; имя —
  `DisplayName.Value` (с заглавной); боеприпасы — `_associatedAmmunitionNames` (те, что есть у юнита,
  `getAmmunitionByName`); раздел по `_weaponType` (CIWS, Gun) и первому боеприпасу (AAW → SAM, ASuW → ANTI-SHIP, ASW /
  RBU / ASROC / торпеда против ПЛ → ASW, торпеда только против надводных → ANTI-SHIP, только удар по суше → LAND ATTACK); место — позиция `_mountObject` в осях корабля
  (fore/aft по z, port/starboard по x, если |x| > 25% максимального). Группа = раздел + имя + набор боеприпасов (×N).
* Домены: AIR (IsAirUnit), MSL (WeaponBase: ракеты и торпеды, с 1.1.0), SUR (Vessel, всплывшая ПЛ), SUB (погружённая ПЛ,
  биология), LND (LandUnit). У пусковой — объединение по её боеприпасам: AAW → AIR+MSL, ASuW → SUR, ASW → SUB,
  `_landAttack` → LND, `_antiTorpedo` → MSL.
* Переключатели игрока: `UnitW.Off[индекс] = маска выключенных доменов` (ConditionalWeakTable по юниту), сохраняются в
  секцию юнита сохранения (`AlternativeWeaponsControl_WeaponAuto=idx:mask,...`, постфиксы ObjectBase.Save/LoadStateFromFile).
* Объединение со Standing Orders юнита (`TacticalDoctrine`): AutoAttackSurface (все пусковые × SUR/SUB/LND, кроме
  LandUnit), ShipsAutoUseSAMsAgainstSurfaceTargets (пусковые с ЗУР AAW+вторичная ASuW × SUR/SUB/LND, свои корабли),
  ShipsAutoUseAntiShipMissilesAgainstSurfaceTargets (пусковые с ракетами ASuW × SUR). Разрешено приказами, если не
  запрещено AutoAttackSurface и хотя бы один боеприпас пусковой для домена не запрещён своим пунктом. Чип: «·» нет
  домена, жёлтый — запрещено приказом, серый — выключено игроком, зелёный — авто; у группы разные — «mixed» (левая
  половина круга зелёная, кольцо зелёное).
  Включить запрещённое приказом: пункт юнита → Yes (IsSet, Value), всё, что от этого включилось кроме нажатого, —
  выключить игроку. Выключить: только Off игрока (серый), приказы не меняются (раньше, если все пары пункта
  выключены, пункт ставился в No — выключенный игроком чип становился жёлтым «off by Standing Orders», и с ним соседние домены
  того же пункта; путало). Смена приказа
  извне (окно Standing Orders, наследование) — по снимку ResolvedValue (`SyncDoctrine`): пары пункта сбрасываются.

## Автоприменение (`WeaponsAi.cs`)
* «Автоматически» = внутри AI.OnUpdate / OnFixedUpdate (счётчик), или внутри ObjectBase.HandleEngageTasks для задач с
  `_isAutoAttack` (все задачи по этим боеприпасу и цели — авто). Постфикс `WeaponSelector.GetWeaponSystemsForAmmunition`
  убирает выключенные для домена цели пусковые (список новый) — AI тогда и боеприпас не выбирает. Ручная стрельба не
  фильтруется.
* CIWS: prefix `WeaponSystemCIWS.AquireTarget` — сбросить текущую цель запрещённого домена и собрать позиции
  запрещённых объектов (угрозы, Vehicles, LocalVehicles); postfix `targetIsInFiringArc` для этого CIWS — false на этих
  позициях. Цель выбирается одна (ближайшая, ракеты раньше самолётов); запрещённая просто не видна в секторе.
* Finalizer HandleEngageTasks: игра раздаёт задачу ещё всем орудиям / CIWS с тем же боеприпасом и связанным пусковым
  (мимо селектора) — авто-задача на пусковой, выключенной для домена цели, снимается (`EndEngageTask`).

## Вид (`WeaponsView.cs`, Noesis кодом)
* Шапка: Free/Tight/Hold (`SetWeaponStatus(status, true)`, Hold ещё `CeaseFire(false)`); «On Tight: engage hostile
  aircraft» — пункт ShipsOnWeaponsTightEngageHostileAircraft юнита (отметка 6×6 и рамка зелёные при «да»); Expand/Collapse
  all — в заголовке колонки WEAPONS слева, по центру по высоте. Заголовки колонок, разделы-карточки.
* Колонки: [шеврон 22, сам шеврон на 2 px левее центра] имя 3* | STATE 2* (60/40; заголовки по центру, содержимое STATE слева:
  точка + текст в StackPanel, MaxWidth текста = ширина ячейки − 14 по SizeChanged, `FitWidth`) | AMMO — по самому широкому «max/max» вкладки (`StockWidth`, мерится TextBlock'ом; заголовок — минимум; +16) |
  FIRE 128 (слоты 64/28/28, пустые сохраняют место) | чипы доменов 26; между WEAPONS | STATE | AMMO — тонкие линии (`Sep`, слева колонки; STATE с отступом 8) (домена нет ни у одной пусковой юнита — `v.DomMask` — колонка 0, чипы и заголовок скрыты). Строки пусковых / боеприпасов — отступ 26 (= Padding 4 строки
  группы + колонка шеврона 22, иначе разделители съезжают), те же 3*/2*. AMMO — по центру. FIRE + чипы — «блок управления»: полупрозрачная голубая полоса `Band` во всех строках, линия слева.
* Строка группы (2 линии): имя пусковой (ссылка → справка системы: `GunMuseumEntries` / `LauncherMuseumEntries` по
  `_systemName`, окно `ObjectInformationViewModel(MainGameView.Instance, entry)` через `WindowHostCommands.OpenWindowCommand`;
  нет записи — справка первого боеприпаса) ×N, в конце первой линии — счётчик каналов (см. ниже); второй линией: один
  боеприпас — имя (ссылка → `WeaponEntry.OpenReferenceCommand`, описание в подсказке; прозвище не показывается), `_displayedType`,
  характеристики (наведение, дальность, скорость, «pwr» = `WarheadData.Power`) — ClipToBounds, одна подсказка на всю
  строку со всеми пунктами («Guidance: SARH», «Range, nm: 2–40», «Max speed, kt: 30», «Warhead power: 20»); несколько — их имена-ссылки. Ссылки подчёркиваются под мышью. Шеврон — Path-треугольник (90° при раскрытии).
* Раскрытие (если есть что показать): строки боеприпасов (если их несколько: рамка с описанием, свой запас, Fire) и
  пусковые группы по одной (метка | состояние | запас | Fire + ■ + ↻ | чипы). Первое открытие для юнита: всё раскрыто; пока
  вкладка не влезает (ExtentHeight > ViewportHeight), сворачивается раскрытая группа, занимающая больше всего места
  (по одной за кадр, `v.Fitting`; игрок раскрыл группу — автосворачивание прекращается). Полоса прокрутки после этого
  пропадает — так и должно быть: всё влезло. Раскрытие запоминается по юниту на сессию (`OpenGroups`). Обновление раз в 0.3 с, пока вкладка видна.
* У раскрытой группы с несколькими пусковыми STATE строки группы скрыт (Hidden): состояния — в строках пусковых.
  Состояние в строке пусковой — одна строка (обрезка «…», высота строки не растёт); цель: целиком → номер → без цели
  (`Shown`), полный текст — в подсказке.
* Состояние (STATE строки группы, `RefreshStates`): одна пусковая или у всех одинаковое (категория, состояние, номер
  цели; тексты, отличные только таймером «мм:сс» менее чем на 3 с, — тоже, показывается меньшее время, `NoTimer`) — одной строкой (перенос на 2), как у одиночной; иначе две — по строке на каждую
  (точка + состояние с целью, обрезка «…», в подсказке «метка: состояние»); больше — счётчики по `Cat` (Inoperable /
  offline-empty / reload / engaging / ready / 5 waiting / 6 sensors off — жёлтый: директоры есть, ни один не включён,
  как «SensorsOff» в GetStatus игры, `SensorsOff`), при нескольких видах — в 2 строки, в подсказке —
  каждая пусковая «метка: состояние → цель» (метки повторяются — с номером #N). Пока у пусковой есть цель (стрельба, наведение, перезарядка в залпе; у CIWS —
  `_currentClosestTarget`) — «→ цель» как её видит игрок (`GetMapVehicle(Globals._playerTaskforce)`: «Track N», опознанная —
  с именем; своя — «Track N (имя)» по `GetMapContactID()`). `StatusParts` → (состояние, цель); перезарядка — «Reloading: мм:сс»
  (в английском тексте игры BottomRow/Reloading пуст). В строке группы цель — только номер (`TrackNumber`); цель, не
  влезающая в место (одна строка у двух пусковых, две у одной и у строк пусковых; `Shown` меряет копией TextBlock), не
  выводится; целиком — в подсказке.
* Запас «текущий/макс»: макс = `_initialAmmoCount` погребов (каждый один раз) + `_maxAmmoCount` пусковых без погреба; у
  отдельных пусковых — заряжено/`_maxAmmoCount`; >50% зелёный, >25% жёлтый, иначе красный; орудия и CIWS с максимумом < 10 —
  одно число без цвета.
* Блок управления: Fire — split: «Fire» → `WeaponEntry.Command` (AttackingState.TryStart, курсор выбора цели), «▾» → меню
  `WeaponEntry.Items`; подсветка частей под мышью; красная, пока игра в AttackingState этого юнита с этим боеприпасом
  (`Globals._mainGameViewModel.StateManager.CurrentState`, проверка каждый кадр). «■» прекратить огонь, «↻» перезарядка
  (только при погребе `hasAMagazine`, активна при `CanForceReload`); свои цвета (красные / синие), значки 12×12.
  Где: строка группы (Fire — если боеприпас один; привязан к пусковой, если она одна), строки боеприпасов (только Fire),
  строки пусковых (Fire + ■ + ↻). Прекратить: `EndEngageTask` у ведущих огонь (CIWS ещё `StopEngage`), из
  `_currentEngageTasks` юнита убираются ждущие задачи с этими боеприпасами — привязанные к этим пусковым, или все, если в
  области все пусковые юнита с этим боеприпасом (`RemoveEngageTask`). Активна, пока есть что прекращать.
* Подсказки без автопереноса: стиль игры (NoesisTheme.Styles.xaml, ToolTip.ContentTemplate) переносит текст на 300 px —
  все строковые подсказки ставятся как свой TextBlock (`TipOf` / `SetTip`: NoWrap, MaxWidth ∞), строки — только явные «\n»
  (у чипов перед «Click:»).
* Подсказки: текст, который не виден целиком (`T()` → `AutoTip` при наведении: обрезан «…», не влез в колонку, срезан
  родителем с ClipToBounds, перенос дал больше строк, чем видно; ширина — по копии TextBlock с тем же шрифтом), получает
  весь текст подсказкой, если у него или у элемента вокруг (8 уровней) нет своей. Против мерцания: ячейка STATE
  группы строится один раз и обновляется на месте (`RefreshStates`), текст/цвет/подсказки/чипы ставятся только при
  изменении (`SetText`, `SetFill`, `Tip`, `Paint`) — пересоздание элементов и повторная установка подсказки каждые
  0.3 с заставляли её мигать.
* Прокрутка: у содержимого нет пустого места внизу (Body без нижнего отступа, у последней карточки отступ 1).
* Закрыли и открыли окно Unit Status: игра делает новый UnitStatusView с тем же статусом, а содержимое вкладки ещё сидит в
  хосте старого окна — HostStep считал, что «его показывает игра», вкладка пустела. Хосты мода — в ConditionalWeakTable `Hosts` (Tag Noesis
  с произвольным объектом не годится — вкладка пустела всегда);
  содержимое из такого хоста забирается в новый.
* Звук щелчка у кнопок и переключателей (`Click(e, a, sound)`; ссылки — без звука): у кнопок игры звука нет — клип в коде
  (AudioClip.Create, 1.9 кГц + шум, 45 мс), 2D AudioSource на объекте плагина, через рефлексию (UnityEngine.AudioModule
  не в ссылках).

## Цель отдельной пусковой (`WeaponsAi.cs`)
* Fire / ▾ пусковой ставит `Pending` (юнит, пусковая, её боеприпасы). Клик по цели создаёт приказ AttackTask в `_userRoot`
  сразу (и на паузе), задачу EngageTask — когда приказ выполняется: postfix `AttackingState.AddAttackTask` привязывает к
  пусковой новый приказ юнита (`OrderPins`), postfix `AttackTask.OnExecute` передаёт привязку созданной задаче (последняя
  задача юнита с этим боеприпасом и целью, `Pins`). Так на паузе можно назначить несколько пусковых на разные цели.
  Pending живёт, пока у игры AttackingState этого юнита, и 3 с после (30 с, если выбор цели так и не начался).
* Селектор: у задачи с привязкой — только её пусковая (нет в списке — задача ждёт). Игра отдаёт новую задачу ВСЕМ орудиям
  с тем же снарядом (и CIWS, связанным пусковым), затирая их стрельбу: если у юнита есть привязанные задачи, prefix
  HandleEngageTasks запоминает поля стрельбы всех систем (`EngageFields`), finalizer тем, кому досталась чужая
  привязанная задача, возвращает прежнюю стрельбу (если была), иначе EndEngageTask.
* «Breaks off» не даём: селектор помечает текущую привязанную задачу (`_hetPin`), prefix `WeaponSystem.EndEngageTask`
  внутри HandleEngageTasks этого юнита пропускает вызов для всех систем, кроме её пусковой. Ручной приказ автоматику не
  вытесняет.
* Сохранение миссии: игра пишет задачи юнита секциями `<юнит>EngageTask1..N` — сначала задача каждой системы, что её
  исполняет (взятая несколькими орудиями — по разу на орудие, UID повторяются), потом список — и при загрузке кладёт все в
  список в том же порядке (ObjectBase.Save/LoadStateFromFile, EngageTask.CreateFromFile). Мод пишет в секцию юнита
  `AlternativeWeaponsControl_PinTasks=N:индекс пусковой,...` (N — номер секции, `SavePins` повторяет порядок игры), при
  загрузке привязывает задачи из этих секций (последние N задач списка, `LoadedTasks`, `Pin`). Старый ключ
  `AlternativeWeaponsControl_Pins=UID:индекс` тоже читается. Привязка ещё не выполненного приказа (AttackTask, `OrderPins`)
  не сохраняется.
  Ошибка игры: юниты создаются и загружаются по одному, цель задачи (`Target=UniqueID`), созданная позже юнита, не
  находится (FindObjectById → null) — задача становится задачей по точке (0,0,0), цель теряется (так было у Свердлова в
  111.sav: цели — корабли противника, создаются после). Мод запоминает такие задачи (`FixTargets`) и в первые кадры
  миссии, когда цель появилась, пересоздаёт задачу по цели (тот же боеприпас, залп, приоритет, флаги, UID, время создания;
  то же место в списке; привязка к пусковой переносится) — `FixTargetsStep` в Update, до 300 с; для всех задач, не только
  привязанных, независимо от галочки мода.
* Несколько привязанных задач в одной раздаче (загрузка сохранения — все сразу в списке): игра отдаёт каждую задачу
  орудий ВСЕМ орудиям с этим снарядом, взявшее свою задачу орудие получает поверх следующую, finalizer её снимает, а
  взятая задача уже ушла из списка — терялась. `HetEnter` запоминает привязанные задачи списка (`_pinnedIn`), `HetLeave`
  возвращает в список те, что не в списке и не у своей пусковой; раздаются снова на следующих кадрах (по одной за раз).
* Ограничения: залп больше её запаса не перейдёт на другие; цель вне сектора — выстрела не будет; приказы с нижней панели
  пусковую не выбирают.

## Каналы управления огнём
* Директоры = `_vwp._associatedSensors`. Директор нужен, только если `ObjectBase.DoesEngageRequiresSensor(ap, цель)`
  (снаряды, SARH для этой цели, радиокомандная коррекция); пусковая без директоров — «free to fire». Ограничивают только
  радарные (`SensorSystemRadar`): нужен директор (не Inoperable, IsOn или `_autoEnableSensor`, `CanGuideAmmo`) со
  (свободным T или уже на этой цели `IsAimedAtTarget` / `_preLaunchTargetObject`) и свободным W
  (`_associatedWeapons.Count < EffectiveWeaponChannels`). Сонар / оптика каналами не ограничены.
* Игра: задача игрока без годной пусковой не снимается (только авто — по возрасту); ракетам при занятых каналах отдаёт
  пусковую с `ignoreSensors` — та берёт задачу и ждёт канал сама; пусковая занята — список пуст, и игра «breaks off» чужую
  стрельбу (орудия; пусковые, если цель — ракета).
* Счётчик в строке группы: рамка «T занято/всего · W занято/всего» по уникальным радарным директорам группы (боеприпас
  `_requiresGuidance` или Projectile; W — только для управляемых), жёлтый, если T или W кончились; подсказка — по
  директорам. Проводные торпеды (`_midCourseCorrection.IsWireGuided`, `_maxWireCount` < MaxValue) — «Wires x/y».
  Счётчик — сумма по директорам: в конфигах (510 кораблей) директоры почти везде альтернативы со своими каналами,
  `TargetTrackSystems` ни один корабль не задаёт.
* Окно директоров: наведение на рамку T/W (0,25 с) открывает окно в слое `v.Overlay` (Canvas поверх вкладки, в её же дереве —
  тот же масштаб, клики работают), под рамкой (над ней, если не влезает); держится, пока мышь над рамкой или окном
  (закрытие через 0,35 с, `DirWindowTick` каждый кадр). По директору: имя — ссылка на статью энциклопедии
  (SensorMuseumEntries по `_systemName` = IniRef, ObjectInformationViewModel как у пусковых, `OpenEntry`), on/off/damaged,
  каналы живьём (`RefreshDirLines`), кратко параметры (`DirFacts`: TypeText, FireControlGradeText, DetailFacts энциклопедии
  без каналов — Air/Surface/Land/Band..., `_fcSolutionGainRate`). У рамки директоров подсказки нет (у Wires — осталась).
* Привязанная задача (`PinRec`, AttackExecPostfix; проверка в HetLeave, `UpdatePins`): ждёт — «Queued» (пусковая занята)
  или «Waiting for FCR channel» (`NoChannel`); `Cat` 5 «waiting» (жёлтое) с целью. Ждёт до первого выстрела (заряжено
  меньше, чем при привязке, или `_engageState` LaunchingSalvo/Launch/Firing); снять — ■.
* Орудия/CIWS не ждут: `WeaponSystemGun.SetTarget` пробует `TryToAddTarget` директоров, неудача не мешает; точность по
  `GetFireControlSensor()` = лучший включённый исправный директор (`FireControl.ResolveBestDirector`, каналы не смотрит)
  с его `_fcSolutionGainRate`; без директора — местное управление `Globals._gunLocalControlGainRate` 0.6 и ошибка
  ×(1+2·темнота). Пометка у названия оружия (колонка WEAPONS, у строки группы
  — уникальные по пусковым через запятую, у строки пусковой — её), пока у установки есть цель (`FcOf`, `SetFc`):
  директор, которым она стреляет, голубым (орудие — GetFireControlSensor, CIWS — FireControl.ResolveBestDirector, пусковая —
  директор, держащий её цель), или «Local control» оранжевым — директоры есть, но ни один не работает (`LocalControl`: не
  Inoperable, IsOn, CanProduceContacts); без директоров вовсе — пометки нет. В STATE больше не пишется.
* GunChannelLimit (false): postfix `WeaponSystemGun.GetFireControlSensor` (только орудия; CIWS — свой
  `FireControl.Classify`): выбранный директор не держит цель (`IsAimedAtTarget`) → другой работающий, который держит;
  нет — `TryToAddTarget` на каждом вызове (`_isGuiding`, `_targetObject` как в activatePreLaunchGuidance); не вышло — null
  → местное управление. «local control» при включённой опции учитывает и это. `ref object __result` при ссылочном типе
  возврата Harmony принимает. Игра в `FreeSensors` (конец стрельбы орудия) очищает ВСЕ цели его директоров — остальные
  орудия перезахватят канал на следующем вызове.
* GunOneDirector (true): игра в `WeaponSystemGun.activatePreLaunchGuidance` делает `TryToAddTarget` на ВСЕХ директорах
  орудия (Свердлов: Fore #1 занимает Top Bow №1 и Four Eyes №1; Adelaide: Mk75 занимает Mk92 и STIR). Prefix запоминает у
  директоров (если их ≥ 2): держал ли цель, `_isGuiding`, `_restPositionReached`, `_targetObject`, `_currentShootingMode`;
  postfix оставляет цель одному — тому, что держал раньше, иначе лучшему из новых (радар, затем больший
  `_fcSolutionGainRate`), у остальных цель из `_associatedTargets` убирается и поля возвращаются. CIWS не трогаем.

## Картинка Workshop
`Release/thumbnail.jpg` 512×512 JPEG в стиле ADM / Smart Windows (тёмно-синяя сетка, рамка-панель, корабль, крупный заголовок,
две строки): панель Weapons (строки с состояниями и чипами AIR..SUB, рамка T 1/1), корабль, каждая установка — по своей
цели. Рисуется скриптом PIL `/home/claude/tools/thumb_awc.py [out.jpg]` (в репозитории нет; DejaVu Sans, рисуется ×2).

## Корабли: погоня за целью ([Ships])
* Корабль, у которого цель вне дальности оружия, разворачивается на неё и даёт полный ход: `AlignUnitToTarget.GetValidHeadingArcs`
  ставит `_goingIntoFiringRange` и дугу ±2° на цель (`Vessel.UpdateAlignmentSteering`: руль, telegraph 5). Задача
  выравнивания создаётся в `ObjectBase.HandleEngageTasks` для каждой задачи с оружием без `_engageOnlyInsideArc`.
* NoRbuSurfaceChase (true) / NoAirChase (true): postfix `GetValidHeadingArcs` (`ChasePostfix`) — у Vessel (и ИИ противника),
  если идёт «в дальность» из-за того, что цель ДАЛЬШЕ максимума, и цель надводная (не погружена) при боеприпасе RBU, или
  воздушная (`isAirTarget`: самолёты, вертолёты, ракеты) — `_goingIntoFiringRange = false`, дуг нет (null): корабль держит курс.
  Разворот от слишком близкой цели и наведение дуги пусковой на цель в дальности — как в игре. Только при включённом моде.

## Gun effects (machine gun tracers / sound, hit sound): moved to Alternative Special Effects

## Кнопки внизу вкладки Weapons
* Кнопки внизу вкладки Weapons перестали не нажиматься сами; `AddClickProbe` (Debug) оставлен.

## Очередь задач (следующий релиз, от пользователя, Oct 20)
1. Опция: наводить туррели на цель в 3D даже за максимальной дальностью (AlignMaxRange = WeaponMaxRange * 1.5 + 2 nm).
2. Кнопка готовности ракет ЗРК на пилонах: под треугольником группы (или вместо него, если пусковая одна); клик — принудительно
   зарядить; таймаут 15 мин, затем ракеты разряжаются; таймер мелким шрифтом; дизайн как у Fire/Cease fire/Reload.
3. Опция: автозарядка ракет ЗРК при приближении неизвестных воздушных контактов (с учётом ООДА).
4. Проверить: почему у пушек вблизи точность хуже, чем на средних дистанциях.
5. Fire подсвечивает зоны досягаемости оружия на карте и в 3D (как Alt + наведение на оружие в статусной строке).
6. Опция: вспышки света при стрельбе и попаданиях в 3D.
7. Опция: по умолчанию обычный режим просмотра в «режиме бинокля» при макс. увеличении; доп. опция — только днём.
8. Закладка Weapons для самолётов и наземной техники.
9. Перетаскивание групп разных видов по вертикали (смена порядка).

## Версия
* 1.0.0 — Steam Release (коммит 5e2fc90): выделен из Smart Windows; шестерёнка и (i) на кнопке вкладки.
* 1.1.0 (в работе): противокорабельные торпеды — в ANTI-SHIP; торпеды как цели — домен MSL; описания в подсказках
  ссылок; опции [Ships] NoRbuSurfaceChase / NoAirChase; отладочный зонд кликов (кнопки внизу вкладки не нажимаются —
  причина не найдена); автопушки «очередь через раз» и звук попаданий — не решено, см. «Открытые вопросы».
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе.

## Кнопка готовности ЗРК (WarmUp.cs, WeaponsView.cs: WarmButton)
- Только `WeaponSystemLauncher` с `_vwp._hasWarmUp && _hasWarmUpAmmo` и магазином. Рельсы: `_containers[i]._weapons` / `_attachmentPositions` -> по иконке ракеты на каждую позицию (многобалочные).
- Состояния: серая (рельсы пусты), янтарная (идёт зарядка: Pending/_isReloading/_state!=Idle или заряжено частично), зелёная (все рельсы).
- Клик по серой: `Charge` = `WarmUp()` + Pending; в postfix `WeaponSystemLauncher.OnUpdate` после конца перезарядки `_ammoForEngage = PickAmmo` + `triggerLoadSystem()`; таймер (SamReadyMinutes) стартует, когда рельсы заряжены. Пуск (рельсы пусты) - таймер сбрасывается, стартует заново после новой зарядки. По истечении без использования: `Discharge` (container.unload(true) - ракеты в магазин, IsHot=false). Клик по зелёной/янтарной: `Discharge` (не в бою).
- Reload для таких пусковых (`CanForceReload` у launcher всегда false): `Recharge` = Discharge + Charge; активна, если пусковая не в бою.
- Не проверено в игре: загрузка через triggerLoadSystem без цели (`_ammoForEngage` ставим сами).
- Опции: [UI] SamReadyButton (true), SamReadyMinutes (15).
- Авто-заряжание ([Ships] SamAutoCharge=true, SamAutoChargeRange=40 nm): postfix `AI.OnUpdate` (WarmUp.cs AutoChargePostfix), раз в 3 с, все юниты (и AI), не Hold. ООДА: берём только `AI._processedObjects` (контакты, прошедшие Observe/Orient). Контакт: `IsAirUnit`, есть на плоте таксфорса, не опознан (`Identified` false или relation Unknown), ближе дальности, сближается (скорость·вектор<0). Заряжаются пусковые с доменом AIR, чип AIR не Off, холодные: `Charge`. Опознанные враги - как в игре (AI.cs ~713 WarmUp).
- SamKeepLoaded ([Ships], true): игра при простое пусковой (rest position, `_systemState==SystemClosed`, !_hasPreloadedAmmunition, `_ammunitionOnWeapon!=null`) ставит `_triggerShutDown=true` -> `unloadSystem()` выгружает рельсы (2-я ракета «пропадает» после 1-го пуска). Postfix `WeaponSystemLauncher.OnUpdate` сбрасывает флаг, пока `IsHot` и state Idle. Остывание IsHot игрой - 1800 с покоя, наша метка - SamReadyMinutes.
- Авто-заряжание: учитываются и опознанные Hostile (игра для них только греет, рельсы не загружает).
- Анимация ракет (SamAnimFix, true): трейс показал `alp=(0,0,0)/(0,0,0)` у load/unload анимации SA-N-1A: в `ammunition/wp_sa-n-1*.ini` `[Launchers]` есть только `SA-N-1`, имя системы пусковой `SA-N-1A` не находится (`TryGetValue` false -> default struct) -> MoveMissileUp/Down двигает с 0 в 0, ракеты появляются/исчезают. Фикс: postfix `setNextAmmunitionLauncherPositions` (загрузка) и `playUnloadAnimation` (выгрузка) подставляют ближайший ключ (префикс имени) в `anim.setAmmunitionLoaderPositions`.

- Диагностика кнопки ЗРК/анимации (warm state/anim/data, auto charge) убрана после подтверждения; остались только Trace при ошибках и отсутствии методов. Данные мода Task Force Grom: в ammunition/wp_sa-n-1a/1b/1c/1d, wp_sa-n-3a, wp_rgb-12 добавлены [Launchers] под имена систем (SA-N-1A.. SA-N-3A, RBU1200).

## Прицеливание за максимальной дальностью (GunFixes.cs, [Guns] AimBeyondMaxRange=true) - пункт 1 очереди, СДЕЛАН
- `WeaponSystemGun.AlignTurret` сразу `return false`, если дальность > `_ammoForEngage._ap._launchRangesInUnity.y` - туррель не двигается. Prefix (только юниты игрока): если max < dist <= 1.5*max + 2 nm - `targetPosition` заменяется точкой на том же азимуте на 0.98*max (поворот/возвышение делает код игры); postfix принудительно `__result=false` (не стрелять). 2 nm = 2/0.036285 юнитов.
- Для воздушных целей дополнительно действует прямой лимит `GunBallistics.DirectFireRangeUnity` внутри метода.
- CIWS и пулемёты (Type=CIWS): prefix `WeaponSystemCIWS.FireAtTarget` (CiwsAimPrefix): в зоне (max + CiwsPrepMargin, 1.5*max + 2 nm] - `CalculateAimPosition` + проверка дуг + `alignToTarget(aim,false,0,false)`, оригинал пропускается (не стрелять). Ближе - код игры. Пусковые - не сделано (свой код).
## Очередь: осталось 9 (1-8 сделаны).

- AimBeyondMaxRange: зона = max(1.5*max + 2 nm, 5 nm) (`AimLimit`). Для воздушных целей max ограничивается `DirectFireRangeUnity*1.15` (prefix получает `isAirTarget`), иначе внутри игрового лимита наведение всё равно отказывалось.
- Формула зоны упрощена: max(1.5*max, 5 nm) (без +2 nm).

## Максимальная точность стрельбы (GunFixes.cs, [Guns] MaxAccuracyMeters=5) - пункт 4 очереди
- Причина худшей точности вблизи: `WeaponSystemGun.GetMeasurementRangeSigma` = max(Globals._gunFCBaseRangeErrorFloorUnity (0.595 units ~ 40 м), 1.5% дальности) / gain^2 * освещённость * (1-0.7*solution). До ~1.4 nm доминирует постоянный пол - это ошибка СУО, не орудия. Разброс самого орудия (CEP / таблица) считается отдельно (DeviateByCEP).
- Опция: на время вызова пол подменяется значением в метрах (prefix/finalizer `RangeSigmaPrefix/Finalizer`), 1 unit = 0.036285132 nm. Масштаб с дистанцией (1.5%) не меняется. ~40 = как в игре.

## Fire показывает зоны (Zones.cs, [UI] FireShowsZones=true) - пункт 5 очереди, СДЕЛАН (не проверено в игре)
- Игра рисует секторы огня и дальность (карта: WeaponArcSegmentViewModel; 3D: WeaponArc3DRenderer.Show) через `WeaponArcHoverManager`: `Hovered` = `_candidate` только при зажатом Alt (private static `Reconcile` каждый кадр).
- Мы создаём свой `HoverInfo` (Unit, Systems, MapRangeInUnity = ammo._ap._launchRangesInUnity.y), prefix `Reconcile` держит его в `Hovered` и вызывает `Show`, пока идёт выбор цели (`CheckTargeting`: AttackingState для этого юнита); `SetCandidate` с нашим owner запускает тик менеджера; по окончании `ClearCandidate(owner)` и штатный Reconcile убирает подсветку.
- Z-порядок на карте: клин сектора (`WeaponArcSegmentViewModel`, `MapPie`) лежит на слое 1 (UNASSIGNED_INFO), кольцо дальности выбранного юнита (`RangeOnMapViewModel`) - на слое 19 (UNIT_MAP_RANGE); `GizmoLayer.AddGizmo` берёт Z-index = номер слоя, поэтому кольцо перекрывало сектор. `ArcZStep` (Zones.cs, из `Update`): после старта Fire ищет `MapPie` с DataContext `WeaponArcSegmentViewModel` в дереве UI (корень - от видимого статус-вью) и ставит Z-index 20 (между 19 и конусами ГСН 21); слой/видимость не меняются, до 25 попыток с шагом 0.2 с. НЕ делать Harmony-патч `GizmoHost.UpdateHost` - игра падала на старте. Alt-подсветка не затронута. Не проверено в игре.
- Пусковая/орудие с Fire на одной пусковой - зона только её (`Pending.Ws`), иначе все системы боеприпаса (`WeaponEntry.WeaponSystems`).

## Flashes, fire light, binoculars normal view: moved to Alternative Special Effects

## Всплывающие сообщения игры блокируют клики - вынесено в Smart Windows
- Причина «кнопки не нажимаются» (подтверждена пользователем): `ItemsControl` над `MainGameViewModel.TopAlerts` (правый нижний угол) ловит клики погасшими сообщениями (hit-test не учитывает Opacity); Smart Windows 1.5.2 с отрицательным ZIndex окон поставил его над окнами. Исправление (IsHitTestVisible по сообщению: `AlertTextOpacity > 0.05`) перенесено в Smart Windows; из AWC убрано. Код был в коммите «Next release work…» (AwcPlugin.cs `AlertsStep`).

## Вкладка Weapons для наземной техники ([UI] WeaponsTabGroundUnits=true) - пункт 8, часть 1 (не проверено в игре)
- `WeaponsAvailableFor`: кроме Vessel/Submarine пропускается `LandUnit` (у него `_obp._weaponSystems` с Gun/Missile/CIWS, как у кораблей) при наличии лаунчеров. Остальной код (модель, AI-хуки, Fire, переключатели доменов) общий: `ItemApplies` для сухопутных - только пункт AutoAttackSurface не применяется, строка Tight-режима только у Vessel.
- Наземные: подтверждено пользователем («вроде все хорошо»).
- Самолёты и вертолёты ([UI] WeaponsTabAircraft=true, часть 2, не проверено в игре): у них ОДНА `WeaponSystemHardpoint` (много `Station`), боеприпасы разных типов в `_associatedAmmunitionNames` (через `addAmmunitionToWeaponReference`) и в `_obp._launcherAmmunitionGroups` (топливные баки - `_fueltankAmmunitionGroups`). `Launchers()` для Aircraft/Helicopter берёт Hardpoint как один Launcher (секция 7 STORES, боеприпасы с доменом 0 - баки, буи - пропускаются), группа с несколькими боеприпасами = AmmoCard с Fire на каждый тип. Переключатели доменов - на всю гардпоинт-систему по домену ЦЕЛИ (SUR off блокирует всё оружие по надводным целям, AAM по воздушным остаются): `WeaponSelector.GetWeaponSystemsForAmmunition` postfix и AI-хуки общие.

## Переключатели в карточках боеприпасов (WeaponsView.cs AmmoCard, AmmoChipsUI)
- В карточке боеприпаса группы (несколько типов) - копии переключателей AIR..LND (кружки 10 px): показаны для доменов, которые этот боеприпас может поражать (`AmmoDom` лаунчеров в scope), состояние = состояние группы, клик = `ClickGroup` (переключает ВСЮ группу). Отдельное состояние по боеприпасу не делали (слишком сложно: `Off` по индексу системы): решение пользователя.
- Cease fire в карточке боеприпаса (`Controls(..., fire:true, more:true)`): гасит задачи только этого боеприпаса (`CtrlUI.Ammo`); `Cease` не прерывает движок системы, если она сейчас стреляет другим боеприпасом из той же системы (`cu.Ammo.Count < l.Ammo.Count` и `_ammoForEngage` не в списке). Cease группы (в строке группы) остаётся - гасит всё.
- Заголовок группы с несколькими боеприпасами без кнопок Fire/Stop/Reload (они в карточках боеприпасов) - решение пользователя.
- Пушки самолёта (F-5A: 2 x M39, `Type=CIWS`, магазины по 280 из `[WeaponMagazineM39_N]`; `MaxAmmoCount` не задан -> умолчание игры 10000): в строке мount показывалось «280/10 000» и в статусе игры «Ready (280/10000)». Теперь при `_maxAmmoCount >= 10000` и магазине - запас = `Stock/StockMax` (280/280), а «/10000)» в тексте статуса вырезается (`StatusParts`).

## Перетаскивание секций по вертикали (WeaponsView.cs MakeDraggable, [UI] SectionOrder) - пункт 9 (не проверено в игре)
- Заголовок секции (SAM / GUNS / STORES ...) - ручка: зажать, тянуть вверх/вниз (карточка идёт за мышью через RenderTransform + capture), отпустить - карточка встаёт на место по центру (позиции по ActualHeight+Margin), отступы пересчитываются (6 px, у последней 1). Сдвиг < 6 px - клик, ничего не делает.
- Порядок общий для всех юнитов, строка имён секций через запятую в [UI] SectionOrder (скрыт из окна настроек); пустая = по умолчанию; секции, которых нет в списке, идут после, по умолчанию.
## Очередь: все 9 пунктов сделаны.

## Settings window tabs
SettingsWindow.cs: each Field has Tab + Group. Tabs: Weapons panel (Panel, SAM readiness button), Fire Control (SAM, Fire control), AI (On Free: unidentified aircraft, Chasing), Guns (Accuracy), Debug. Tab bar is row 1 of the window; a yellow dot on a tab = unapplied changes inside it. Current tab is kept while the game runs.

## Локализация (Loc.cs, Release/lang/*.ini)
- Все тексты мода - в таблице `D(key, en)` в Loc.cs; в коде `Loc.T(key)` / `Loc.F(key, args)` (`{0}`...). Тексты .cfg-описаний (подсказки окна настроек) регистрируются в рантайме как `help.Section/Key` (`Loc.Register`), подписи полей окна - `lbl.Section/Key`, вкладки `tabs.*`, группы `grp.*`.
- Файлы `lang/<код>.ini` рядом с dll (формат `key = text`, `;` комментарий, `\n` перенос). Мод сам создаёт файл для каждого языка игры (список - config.ini [Languages] через `Utils.openIniFile("", "/config.ini")`) и en.ini; в существующий дописывает отсутствующие ключи после `; --- new ---`, ничего не меняя в правках игрока. Нет перевода/файла - английский.
- Язык: `LanguageResourceHandler.Language` игры (проверка раз в 1.5 с) или `[General] Language` в .cfg (вкладка Language окна настроек: клик циклично переключает язык игры / коды).
- Release/lang/*.ini (en, ru, cn, de, es, fr, ko, ja, vn - коды языков игры из config.ini [Languages]) генерируются `/home/claude/tools/gen_lang.py` (ru.json + langs/*.txt, позиционные списки по ключам). Подсказки help.* - langs/help_<код>.txt (29 строк, порядок как в help.json).
- Не переведено: названия из игры (ItemLabels = подписи Standing Orders, имена оружия) - они приходят из локализации игры.

- Перетаскивание секций: остальные карточки сдвигаются прямо во время перетаскивания (RenderTransform по снимку раскладки на MouseDown, WeaponsView.MakeDraggable).

## Выбор активного боеприпаса (группа с несколькими боеприпасами и несколькими пусковыми)
- WeaponsView.AmmoCard: круглая кнопка-селектор перед названием боеприпаса (клик по ней или по рамке карточки), активная карточка с голубой рамкой. `GroupRow.ActiveAmmo` - запоминается для юнита на время сессии (`ActiveMemory`, ConditionalWeakTable по объекту юнита + ключ группы), в сохранения не пишется. По умолчанию - боеприпас, уже готовый в пусковой (`_ammoForEngage`), иначе заряженный, иначе первый (`DefaultActive`).
- `LayoutDetail`: под карточкой активного боеприпаса - строки пусковых, умеющих его применять; остальные (приглушены, Opacity 0.5) в конце. Fire пусковой стреляет активным (`MemberRow.Ids` = [active], список общий с `FireUI.Ids`/`CtrlUI.Ammo`); Stop пусковой по-прежнему гасит любые её боеприпасы (`CtrlUI.Ammo` = все).
- Ключ перевода `ammo.activeTip` (последний в списке ru.json / langs/*.txt).

## Названия пусковых (WeaponsModel: Launchers, ArcCentre)
- Раньше нос/корма и борт считались по положению `_mountObject`. У подлодок (Romeo, plan_ss_romeo.ini) все торпедные аппараты с `Mount=Dummy` стоят в одной точке корпуса (разброс < 1 ед.), поэтому все были «Fore», а одна с микросмещением по x - «Fore starboard». Кормовые аппараты (`[WeaponSystem7/8] #Aft`, `PreferredArcs=90,-90`, магазин `..._Aft`) не отличались.
- Теперь: по положению (как раньше, с борт-суффиксом), КРОМЕ наборов одноимённых пусковых (>1 шт.), стоящих в одной точке (разброс мировых позиций < 0.03 ед. (1 ед. = 67 м, AimNmPerUnit); версии с порогом «1 ед.» и с 2 % bounds Renderer сломали названия кораблей: Prj.159 «AK-726 Gun #1», «Port #1/#2»); для таких - по центру `_vwp._preferredArcs[0]` (-90,90 -> центр 0 -> fore; 90,-90 -> 180 -> aft; около ±90 -> port/starboard). Подписи одинаковых получают «#N».
- Подписи сокращаются: часть (fore/aft или port/starboard), одинаковая у всех пусковых одного имени, отбрасывается: «Fore port / Fore starboard» -> «Port / Starboard»; «Fore port / Aft port» -> «Fore / Aft»; «Fore / Aft» без бортов остаётся.
- Место пусковой = мировая позиция `_containers[0]._gunObject` («AmmunitionParent», локальная позиция = `ContainerN_Position`), иначе `_mountObject`. Причина: у подлодок все аппараты имеют один `Mount=Dummy`, а положение носовых (z=+0.44) и кормовых (z=-0.43) задано только в Container1_Position (plan_ss_romeo.ini [WeaponSystem7] `Container1_Position=0.0094,-0.0002,-0.426`) - кормовые аппараты Prj.633 были «Fore». Бортовая часть подписи - только при |x| > 0.02 ед. (миллиметровые смещения - не борт).
- Кнопка готовности ЗРК (WarmButton): раньше стояла в колонке шеврона (22 px) - иконки двух-трёхбалочной пусковой (M-1 Volna) не влезали и выступали за край. Теперь `inline` только для одиночной пусковой: в строке названия (после названия / ×N, до значка каналов), таймер справа от кнопки, отступ сверху 1 px. У строк-членов группы кнопка осталась в колонке шеврона (Margin -20), как было.

## Остаток на пусковой (stock.rail)
У одиночной пусковой (не орудие), у которой есть направляющие (`Rails(ws)`: слоты контейнеров), под общим остатком группы маленькой строкой «На пусковой N/M»: сколько ракет сейчас на направляющих / сколько слотов. Общий остаток (`Stock`) - всё, что есть у пусковой (у Mk 29 без отдельного магазина это `_maxAmmoCount`, 16/24). Первая версия требовала `_associatedMagazine` и у Mk 29 не показывалась. Не проверено в игре.

## Сокращение типа наведения (ShortGuidance, WeaponsView.cs; lang-ключи guid.<GuidanceType>)
Название из игры (`GuidanceTypeText`, ui.ini [GuidanceType]; части через ", ": основной тип + HomeOnJam) - если ВЕСЬ текст (с обеими частями) длиннее 15 символов, каждая часть заменяется кратким видом из lang-файла (`guid.IRHoming`, `guid.SemiActiveRadarHoming` ... `guid.HomeOnJam`; en: SARH, ARH, ARM, ASH...; ru: ПАРГСН, АРГСН, ПРГСН...). Пустое/удалённое значение - показывается название игры. Полное название - в подсказке (`spec.guidance`). Автосокращение по буквам убрано (давало коллизии). Сокращения других языков подобраны мной и не проверены носителями. Не проверено в игре.

## Остаток "всего" у пусковых без ёмкости (Cap, WeaponsView.cs)
У Mk 141 (Harpoon, weapons.ini `[MK141]`, `ReloadTime=0`) `_maxAmmoCount` = 0 и магазина нет: игра считает заряженными те ракеты, что на направляющих (`loadAmmunitionNoMagazine`, `WeaponContainer.LoadAmmo`). `StockMax` давал 0 -> показывалось просто «8», без «/8». Теперь `Cap(ws)`: `_maxAmmoCount`, а если 0 - число слотов направляющих (`Rails(ws).Count`). Не проверено в игре.

## Перетаскивание групп: куда встаёт карточка (DropIndex, WeaponsView.cs)
Раньше сравнивались центры (центр перетаскиваемой карточки с центрами остальных) - «толстую» группу приходилось тащить далеко. Теперь верхняя граница перетаскиваемой карточки сравнивается с верхними границами мест (список без неё, карточка вставлена на каждый индекс): берётся ближайшее. Сдвиг на соседа начинается примерно при смещении в половину его высоты, высота самой карточки не важна. Один и тот же расчёт для живого сдвига (`slide`) и для итогового места. Не проверено в игре.

## On Free: engage approaching fast unidentified aircraft (FreeAir.cs)
- Флажок под «On Tight» в шапке вкладки (только корабли, `UnitW.FreeAirSet`: -1 = по умолчанию стороны, 0/1 = выбор игрока; галочка оранжево-красная); хранится в сейве юнита (`AlternativeWeaponsControl_FreeUnknownAir=0/1`, только если выбрано явно; ObjectBase.Save/LoadStateFromFile). По умолчанию (`FreeAirOn`): выключен у кораблей игрока и союзников, ВКЛЮЧЁН у кораблей противника (`Taskforce.Side == Enemy`, их ИИ тоже получает эти цели). Это НЕ доктрина игры (у «On Tight» это `ShipsOnWeaponsTightEngageHostileAircraft`).
- Причина, почему игра не стреляет по неопознанным: `AI.GetPossibleTargetsList` берёт юнит только если `IdentifiedAsHostile` (`Vehicle.CurrentRelationship() == Hostile` на плоте игрока).
- Postfix `AI.GetPossibleTargetsList` (`FreeAirPostfix`): для корабля (игрока и ИИ) на Free с флажком из `_processedObjects` добавляет в `_possibleTargetsWithPriorities` самолёты/вертолёты, которые (1) на плоте ещё `Unknown` (настоящая принадлежность НЕ проверяется - без читерства: опознанные свои/нейтралы отсеяны самим `Unknown`, неопознанные свой/нейтрал, подходящие по скорости и курсу, будут атакованы - штраф игры, решение пользователя; свой taskforce в `_processedObjects` не попадает), (2) быстрее [Fire] UnknownAirMinKnots (500; крейсерская гражданских Mach 0.8-0.84 ~ 460-480 уз, максимум Mach 0.98 ~ 560 уз на высоте), (3) ближе 50 nm (`MaxRangeUnits`, 1 ед. = 0.036285 nm), (4) летят на корабль (угол между скоростью и направлением на корабль <= 45°). Скорость узлы = |UnityVelocityVector| / 0.0076554087. Порог только в .cfg (в окне настроек нет).
- Не проверено в игре: берёт ли цель дальше `HandleEngageTasks`/оружие без идентификации (CIWS проверяет `CurrentRelationship == Hostile` сам и по таким целям не откроет огонь).

- Флажки шапки (On Tight / On Free) стоят в строке (`WrapPanel` в растягивающейся колонке, переносятся на 2-ю строку только при нехватке ширины). Текст On Free сокращён («On Free: engage unidentified aircraft»), подробности - в подсказке `tab.onFreeTip` (порог из `[Fire] UnknownAirMinKnots`).
- Порог скорости On Free теперь и в окне настроек (вкладка AI, группа «On Free: unidentified aircraft», `Fire/UnknownAirMinKnots`, 100..2000 kn); из подсказки флажка убраны строки про умолчания и .cfg.
- On Free, окно настроек (группа «On Free: unidentified aircraft»): `Fire/UnknownAirMinKnots` (500), `Fire/UnknownAirMaxNm` (50), `Fire/UnknownAirDefaultOwn` (false: свои и союзные корабли), `Fire/UnknownAirDefaultEnemy` (true: корабли противника, ИИ). Выбор, сделанный на юните (`FreeAirSet`), главнее умолчаний. Подсказка флажка берёт скорость и дистанцию из настроек при построении вкладки.

## Название группы из одной пусковой, когда есть одноимённая группа (GroupRow.Suffix)
Пусковые одного типа с разным набором боеприпасов (Mk 26: RIM-66C+RUR-5 и только RIM-66C) стоят в разных группах, и названия строк совпадали (подписи положения есть только в строках-членах групп из 2+ пусковых). Теперь у группы из одной пусковой, чьё имя совпадает с другой группой секции, к названию добавляется положение пусковой (`Launcher.Loc`: Fore / Aft ...), без него - «#N». Не проверено в игре.
- ОШИБКА (исправлена): `ObjectBase._taskforce` - СВОЙСТВО (`_innerTaskforceValue`), `R.F` его не видит (null). `FreeAirPostfix` выходил на `tf == null`, поэтому On Free ничего не делал (в trace не было строк `free air`); `FreeAirOn` не определял сторону. Для таксфорса брать `R.V` (поле или свойство). Там же исправлен `CiwsEnter` (WeaponsAi.cs: контакты плота для отключённых доменов теперь попадают в skip, раньше - только `_threats`).
- Settings window: the label column is star-sized (MinWidth 330), so it grows with the window; editor column is star-sized too (MinWidth 200).
- Settings window: height 805 screen pixels (converted by the UI scale at the first opening); language is a ComboBox as in ADM; Apply rebuilds the whole window (title, tabs, buttons) so a new language shows at once.
- Apply: rebuild of the whole window is delayed by 2 frames (SettingsWindow.Tick from Update), not done inside the Apply click.
- Fire: the game's max-range circle (AttackingState MapEllipse with SemiMajorAxis == MaxRangeInLat) is collapsed by RingHideStep (UI-tree scan every 0.25 s while zones are active); the firing-arc wedges get a red Stroke (235,45,45) in ArcZStep.
- Defaults changed to the user's cfg: UnknownAirMaxNm=20, SamAutoChargeRange=30, ClickVolume=0.5.
- Fire zones scan (Zones.cs ArcZStep): every frame for 90 frames after the zones start, then 4/s; all visible roots; hides AttackingState max-range ellipse, wedges red + Z 20 re-applied on every scan (they are rebuilt when the hovered weapon changes).
- Fire zones: Harmony postfix on AttackingState ctor starts a per-frame scan (120 frames) that collapses the max-range circle; the wedges' orange is replaced by changing the colour of the game's resource Map_FiringArc (ArcColorStep, every 1 s, found via FindResource); the per-wedge Stroke is only a fallback if that fails.
- Fire zones, range circle (replaces the scan): Harmony prefix on AttackingState.set_MaxRangeInLat stores the real value (ConditionalWeakTable) and passes 0 (FireShowsZones on) - the MapEllipse never gets a radius, the max label hides with it; prefix/postfix of UpdateBearingPreview put the real value into _maxRangeInLat for the call (line ~742 uses it). The scan of ArcZStep now only sets the wedges' Z-index 20.
- Fire zones, FINAL approach (replaces the zero radius, resource colour and the scan): Harmony postfix ZoneStylePost on MapEllipse.MarkDirty, MapPie.MarkDirty and MapPath.StrokeChanged (property-changed callbacks, so it runs when the control gets its values): MapEllipse of AttackingState with SemiMajorAxis == MaxRangeInLat -> local Stroke fully transparent; MapPie of WeaponArcSegmentViewModel -> local red Stroke + Z-index 20. A local value overrides the DynamicResource brush; the shared resources and the model stay untouched.
- Fire zones, CURRENT: the transparent-stroke patches of MapEllipse/MapPie/MapPath callbacks CRASHED the game at start - reverted to the zero-radius circle (AttackingState.set_MaxRangeInLat prefix + UpdateBearingPreview swap) and the UI-tree scan for the wedges (red Stroke always, Z 20, Map_FiringArc colour changed too). Do not patch the map controls.
- Fire zones, FINAL (no scanning): zero-radius max-range circle (set_MaxRangeInLat prefix + UpdateBearingPreview swap) and red wedges via Harmony postfix of ThemeDictionarySwapper.ApplyTheme, which replaces Map_FiringArc in the dictionary just merged into the map (the theme is local to the map, so FindResource from the status view never saw it). The UI-tree scan and the wedges' Z-index step are gone (the circle was what covered them). Takes effect when the map loads / theme changes, so FireShowsZones is read then. Patching MapEllipse/MapPie callbacks crashed the game at start.
- The game crashed at start with ThemeDictionarySwapper.ApplyTheme postfix changing the dictionary inside the callback: now the postfix only notes the swapper, ThemeSwapStep (Update) changes Map_FiringArc on a later frame. If it still crashes - suspects: that patch, then set_MaxRangeInLat/UpdateBearingPreview.
- LogOutput of the crash ended right after 'Enabled in the game's Mods menu' (patching at start): the ThemeDictionarySwapper.ApplyTheme patch is REMOVED (do not patch it). Wedge colour now: ArcColorOnce at the start of the zones finds the MapBase once (cached), replaces Map_FiringArc in its MergedDictionaries by a red brush.
- The max-range circle is hidden only for a targeting state started by the mod's Fire button / its menu (OurFireExecute wraps the command, OurFireDepth > 0 when the state first gets MaxRangeInLat; marked in RealMax). The game's own Fire keeps its circle.
- Zones (wedges) are shown only for targeting started by the mod's Fire (IsOurState, the RealMax mark); the game's own Fire shows neither the zones nor hides its circle.
- SAM readiness: a launcher made hot and loaded by the game itself (no Box) is adopted (WarmPostfix, 2x per second per launcher): Box with Until = now + SamReadyMinutes, so the button shows the timer and the missiles are put away at the end.

## Performance audit (loops / reflection in hot paths)
- `CheckTargeting` (every frame per visible Weapons tab): the `Globals._mainGameViewModel` FieldInfo is cached (was a type + field lookup by name each frame).
- `UnknownAirClosing` (AutoCharge, every 3 s per AI): `LocalVehicles` is read through `IDictionary` instead of `GetMethod("TryGetValue").Invoke` per object.
- `FreeAirPostfix`: the list of added aircraft is allocated only when there is something to add.
- Checked and left: AiEnter/AiLeave, ArcPostfix (O(1)); AutoCharge and warm-up adoption (throttled 3 s / 0.5 s); HetEnter/HetLeave (per distribution, not per frame); CiwsEnter (only units with a CIWS switched off; builds the skip set per AquireTarget call - a per-frame shared set is possible if profiling shows it); WeaponsTick refresh (0.3 s).
- Not verified in game.

## Section title row: common controls
- (The first attempt, Fire / Stop / Reload in a launcher's own row, was taken back: the ammunition cards already have them.)
- A section (SAM, GUNS ...) with 2+ launchers that differ (several groups, or a launcher with several ammunition types) gets controls in its title row, in the same columns as the rows below: Fire / Stop / Reload and the five target switches. They work for the ammunition that ALL the section's launchers have (intersection); none in common: the title row stays plain.
- Stop (`Cease`) ends only what is fired with the common ammunition. Reload: as the ammunition cards' (the game's ForceReload works on the launcher's current ammunition `_ammoForEngage`; it cannot be aimed at one type). Fire without a pinned launcher uses the first common ammunition; the arrow opens the usual menu.
- Switches: only the target types of the common ammunition (`SecCtl.LDom`); the game's switch is per launcher and target type, not per ammunition, so it covers every ammunition of that type on the launcher.
- Revised after a UI review: shown ONLY when the section has more than one group (a single group's row already has the chips; the ammunition cards have Fire / Stop / Reload). Fire uses the ammunition chosen as active in the groups if it is a common one (else the first common) and its tooltip names it; the switch tooltip says it covers the target type on all the section's launchers (`tip.switchesSection`); the Reload tooltip says the game reloads the ammunition the launcher holds ready (`tip.reloadCurrent`). New keys are in ru.json / langs/*.txt (tools) and Release/lang.
- Code: `SectionControls`, `SecPairs`, `ClickSection` (WeaponsView.cs), painted in `Refresh`. Not verified in game.

## SAM button: ready only when the load is really done (bug report)
- Reported: after Fire the missiles can show the load animation at once, not waiting for the reload to end; the missile button can be green (2 missiles) while the rails look empty and the reload is counting (after a lot of charge / unload games). Root cause NOT found yet; not reproduced.
- Facts from the game code: `spawnWeapons` fills `_containers[i]._weapons` at the START of the load animation, so `Rails()` (our button) counts the missiles as loaded then; `WeaponSystemLauncher.updateLoadSystem` runs every update independently of the reload timer (the vanilla load state machine starts right after a salvo, line ~796 `triggerLoadSystem`), so the animation can precede the end of `_isReloading` even without the mod.
- Display fix: `WarmState` shows "loading" (amber, rails dim) instead of ready/green while the load state is not Idle or `_isReloading` is true.
- Debug trace: with [Debug] on, each change of a launcher's loading state is logged (`sam load ...`: state, reloading, hot, weapons/rails, onWeapon, engaging, shutDown, our box). To be read when the bug is reproduced.
- Not verified in game.

## SAM: a cold launcher loads before its warm-up (found in the trace, vanilla behaviour)
- Trace: Unload with the missile button (hot=False, rails empty), then Fire: `InitiateLoading` at once (engaging=True, hot=False), rails 2/2 after 0.1 s, and only then `hot=True reloading=True` (the warm-up reload, 8 s), launch when it ends. The cause is not the unload: any cold launcher does it (also at the mission start, MK26 weapons=0/2 hot=False). `WeaponSystemLauncher.OnUpdate` waits for the guidance (`AcquisitionComplete`) with `ReadyUpWhileWaiting()`, which calls `getContainer()` -> `triggerLoadSystem()` (load at once); the warm-up (`IsHot = true; StartReload()`) comes later in the "WarmingUp" branch.
- Decision (user): this is a feature of the launch procedure, not a bug. The fix (a prefix on `ReadyUpWhileWaiting` that starts the warm-up first for the player's cold launchers) was written and TAKEN OUT again; the vanilla order stays.
- Not verified in game.

## SAM button: warm-up on the rails (decision)
- Real launcher: the missile is powered and warmed on the rail, not in the magazine; the game's own order for a fire order to a cold launcher is the same (load, then the warm-up count). The button now does the same: `Charge` only marks the box (`Pending`, `Warm`); the game's load system puts the missiles on the rails; when they are loaded and the state is Idle `WarmUp()` starts the reload count (IsHot); the time limit starts when it is over. Before: warm-up first, then the load.
- While loading / warming the button shows "loading" (amber, rails dim): `WarmState` counts a `Pending` box as busy also when the launcher is not hot yet; the box is no longer dropped while `Pending` and cold.
- Not verified in game. Watch: the game's idle "unload the rails" must not fire between the load animation and `WarmUp()` (one frame).

## Trace for "Ready with empty rails / Fire goes to the other launcher" (bug report, not solved)
- From the trace (build 2226): after the last missile of a launcher is launched the rails stay empty, the reload counts, then the launcher is idle/hot with weapons=0/2 (the game loads on demand when the next engage comes: `InitiateLoading` at the order). So "Ready" with empty rails is the game's state; the status text does not say that the rails are empty.
- Which launcher fired could not be read: the lines of the two MK26 launchers have the same name. Added (Debug): `sam load` lines carry the launcher index (#n); `Fire clicked: <ammo>, launcher #n`; `pinned selector <ammo>: launcher #n pinned, the game offered #...` (and a note if the pinned one is not among them).

## SAM: automatic reload of the rails after a salvo (decision)
- The game keeps an emptied launcher hot and loads the rails only at the next order, so "Ready" showed with empty rails. Now a hot launcher that is emptied (Idle, not reloading, magazine has missiles) is loaded again by the same `Pending` load as the button's - but only while the hot missiles are needed (`NeedHot`): the unit has open engage orders for the launcher's ammunition with a live target, OR an unknown / hostile aircraft is coming in within [Fire] SamAutoChargeRange (`UnknownAirClosing`, as the auto charging). Not on "Hold". Launchers made hot by the game are covered by the adoption check (twice a second). The 15-minute count starts when the rails are loaded.
- Not verified in game.
- Cooling (user: "the warm-up is on the rails only"): a hot launcher with EMPTY rails, idle, not reloading, not engaging, when the hot missiles are NOT needed (`NeedHot` false) or the magazine is empty, goes cold (`IsHot=false`, box removed): status grey; the next Fire loads the rails and warms up again. Checked twice a second per launcher. Not verified in game.

## Optimization of the SAM hooks
- `WeaponSystemLauncher.OnUpdate` runs for EVERY launcher (tubes, RBU, guns' launchers) every frame: `CanWarm` is now cached per launcher (`_canWarm`), and a launcher that cannot warm up and has no box returns at once (before: ~6 reflection reads every 30 frames for each, and the samKeep field read every frame).
- A launcher with a box (not loading / unloading) is checked about 4 times a second (15 frames), not every frame; loading / unloading steps stay per frame.
- `MissionTime()` (asked every frame by every AI in `AutoChargePostfix` and by every launcher) was a type lookup + property lookup + reflection call; the getter is bound once as a delegate.
- Not verified in game.

## "Rails empty" instead of "Ready" for empty rails
- "Ready" comes from the game (`WeaponSystem.GetStatus`); vanilla says it for empty rails too (the rails are loaded at the next order).
- `StatusParts`: for a ready (cat 0) launcher with a warm-up, empty rails, no reload / engagement and missiles in the magazine, the text is replaced by "Rails empty" (`SamStatus`, key `st.railsEmpty`). Cooling of a hot launcher with empty rails is done by `WarmPostfix` as before. Not verified in game.

## No pointless reload after the last missile
- After the last missile of a salvo the game counts a reload ("Reloading mm:ss") although the rails are empty and hot missiles are not needed (the launcher cools down anyway). `WarmPostfix` (adoption, once per 30 frames): hot + reloading + empty rails + `!NeedHot` -> `_isReloading=false`, `_remainingReloadTime=0`, `IsHot=false`. With a demand (order / threat) the reload goes on as before, then the automatic load.
- Also the reload must not even start: `LaunchPostfix` (postfix of `WeaponSystemLauncher.launch`, once per launch): empty rails, reload just started, `!NeedHot` -> the reload is dropped at once, IsHot=false. With a demand (an open task with a live target) the reload runs; the check in `WarmPostfix` drops it later when the demand is gone. OSA (ReloadsLowerDeck, state not Idle after launch) is left alone. Not verified in game.

## SAM state model (WarmUp.cs) - one model, replaces the earlier patches
- States: cold (empty rails) -> loading -> warming up (the game's reload, missiles already ON the rails) -> ready. "Hot" = warm missiles on the rails; hot with empty rails = cold.
- Demand = button ON (`Box.Btn`) OR an open task with a live target OR the game's own preparation / warm-up for air targets OR an unidentified aircraft closing in (`NeedHot`, cached for 1 s).
- `WarmPostfix` (= Reconcile; only launchers under control: hot or with a Box; 4 times a second):
  - a reload on empty idle rails is dropped (`GoCold`); with a demand -> `Charge` (load, then warm up on the rails); without one the launcher stays cold;
  - `LaunchPostfix` does the same right after a launch (a reload on empty rails does not start);
  - time limit over: Btn is cleared; with a demand -> unload and recharge, otherwise unload and cold;
  - button OFF = a single unload; if the demand is still there the charge repeats; the Reload button = unload + charge + Btn.
- Status (`SamStatus`): "Rails empty" / "Loading rails" / "Unloading rails" / "Unloading: mm:ss", otherwise the game's text.
- Finding (trace build 2302): the game's rail loading (`InitiateLoading`) sets `_isReloading=true` itself and counts from the LAST `StartReload` (the launch time); after our `GoCold` that gave a second count (launch -> load with an 8 s count -> another 8 s warm-up on the rails). `GoCold` now also resets `_reloadStartTime=-1000`: the load count ends at once, one warm-up on the rails is left.
- Trace 2320: after an unload (button OFF during the warm-up) the game's reload count runs to the end on the empty cold launcher (31.3 -> 36.1). Decision: leave it (waiting after a charge cancelled midway is fine). `ResetReload` is used only in `GoCold`.
- Auto-charge for an air target (`UnknownAirClosing`) is NOT mixed with the "On Free" settings (charging is not firing): its own setting `SamAutoChargeRange` (30 nm); the target is not Friendly / Neutral / Hostile, the distance shrinks by the RELATIVE speed (aircraft minus own ship, horizontal). The old test used the speed over the ground and charged the launchers for a slow unidentified airliner on a parallel course.
- A contact identified as Hostile is not part of OUR auto-charge, but the game warms up for it: AI.cs 705-726 calls `WarmUp()` on ALL launchers every cycle while `_possibleTargetsWithPriorities` holds an air unit (or a missile aimed at a non-air target), with no range check. This is the game's demand and `NeedHot` honours it (otherwise we cancelled the game's warm-up every 4 s: trace 22.10 "reloading=True -> False"). The game puts the rails away when idle; IsHot goes out 1800 s after the last use. There is no speed threshold for charging (a separate setting if needed).
- Debug: `ai targets <unit>: <fire status> | <target> p<priority> <distance>nm;...` - what the game's AI of the unit takes for possible targets (traced when the list changes). Vanilla priorities of a ship: threat 1+, missile 3+, surface 2-5/10, aircraft without a threat 9 - a known hostile aircraft comes AFTER surface targets.
- Not verified in game.

## Target selection by the plot, not the true side (Fire/EngageByPlotRelation, on by default; UI label "Disable AI cheating", ON = fix active)
- `AI.GetPossibleTargetsList` takes a contact unit as a target only if it is hostile on the plot (`IdentifiedAsHostile`) AND its TRUE faction is hostile to the ship's (`RelationshipTo`, AI.cs ~4915). The player cannot know the true side: a neutral airliner marked Hostile by hand never became a target (confirmed with the mod switched off: vanilla does the same).
- `PlotHostilePostfix` (FreeAir.cs, postfix of `GetPossibleTargetsList`): for the player's ships on Free it adds the units that are Hostile on the plot (surface ones only with the AutoAttackSurface doctrine, as in the game), priority `GetEngagePriority`, the list is sorted again (priority, distance). The setting is on the AI tab ("Target selection"). Debug: `ai targets ...` shows the final list. Not verified in game.

## The game's launcher preparation (RequestPrep) is part of the demand
- Trace 0017: the game warms launchers up for an approaching target (`AI.PrepareLaunchersForClosingThreats` -> `WeaponSystemLauncher.RequestPrep` -> `UpdateSilentPrep`: a 2 s window, `IsHot=true` + `StartReload()` on EMPTY rails, then the load). Reconcile did not count it as a demand and cancelled the count every ~4 s (a 0.2 s "Reloading" flicker on Fore); the reset also made the launcher "free" for the AI's next task.
- `NeedHot` now includes `_prepAmmo/_prepTarget` (a live target), and in the "reloading on empty rails" branch the demand is asked without the cache: the game's count is replaced by load -> warm-up on the rails (`Charge`). The choice of the launcher for the 2nd missile is the game's (firing arcs; a launcher with `_executingEngageTask` is not available); the link with the cancelled count is a hypothesis. Not verified in game.

### The game's warm-up for air targets is part of the demand (22.10)
- AI.cs 705-726: after `GetPossibleTargetsList` the game calls `WarmUp()` on all launchers of the ship if the list holds an air unit or a `Missile{_isTargetAirborne:false}`. No range check. Trace 22.10 (Vincennes MK26 #0): every 4 s `reloading=True hot=True box=none` -> 0.1-0.4 s later `reloading=False hot=False` - the game warmed up, we put it out (no demand from orders / prep).
- `NeedHot` now reads `_ai._possibleTargetsWithPriorities` (except on Hold). Debug: `sam cancel: ...` is traced when a count on empty rails is dropped for lack of demand. Not verified in game.

### Order "load animation first, then the reload" for a launcher in an order (22.10)
- The game's order to a cold launcher (WeaponSystemLauncher.OnUpdate ~477): `IsHot=true; StartReload()` - the count runs on EMPTY rails, the load (`InitiateLoading`) only after the count. Agreement: the load animation opens the reload.
- `WarmPostfix`, `IsEngaging` block: `reloading && loaded==0 && Idle && _ammoForEngage!=null && magazine>0` -> `triggerLoadSystem()` (the count is not reset; `updateLoadSystem` is called from `WeaponSystem.OnUpdate` before the `_isReloading` check, so the load runs in parallel with the count - the way the game handles a repeated salvo).
- Trace 22.10: Fore (#0) counted 28.1 -> 36.1, then loaded; Aft (#1) loaded at once.

### Order after an unloading; the CIWS hook (22.10)
1) Trace 22.10 (122-133 s): after our unload the old count was still running (`reloading=True`), the Pending branch waited for `!reloading`, the game's WarmUp started a new count on empty rails and the load animation came after it. Now in the `reloading` branch `b.Pending && loaded==0 && Idle && magazine>0` calls `triggerLoadSystem` (the count is not reset): the animation comes first.
2) `CIWS.FireAtTarget not found` in every trace: the hook looked for the method in `SeaPower.CIWS` (the effects part: tracers, sound; startFire / update are right there). `FireAtTarget` (private, no parameters) is in `SeaPower.WeaponSystemCIWS`; the lookup is fixed. Every member `CiwsAimPrefix` needs exists in `WeaponSystemCIWS` (checked in the decompilation). Time compression extras (MainMod) patches the same method: compatibility is to be checked in game (the user tested: CIWS / machine gun aiming beyond range works).

### Pause after an unloading; a stuck second Reload (22.10)
1) The pause between the end of our unloading (`UnloadStep`) and the next load is half of the launcher's reload cycle (`_vwp._magazineReloadTime * 0.5`, `LoadPause`). Only after an unloading, not after the last launch (now a separate block, see below).
2) Stuck: a second Reload press during the load animation (`WaitForLoadAnimation`, trace 04:11:04) set `_triggerShutDown=true`; while the flag is up `updateLoadSystem` (WeaponSystemLauncher.cs ~1553) returns at once, the state machine froze and `UnloadStep` waited for Idle for ever. `UnloadStep` now sets the flag only when the launcher is `Idle` (it waits for the animation to end).

### Repeated Reload press (22.10)
- Trace 04:24:50-04:25:02: 9 Reload presses in a row on cold empty launchers; each started an unloading (0.04 s on empty rails) and set the pause again - the load started 4 s after the LAST press. Decision (user): while an unloading or the pause before the load is running, a repeated press is ignored and the count is not restarted.
- The Reload button is disabled while the launcher is already reloading (user decision 22.10): `ReloadBusy` (WarmUp.cs) = Box Unload / Pending, `_isReloading`, state not Idle, `_triggerShutDown` - the whole cycle up to "ready", and a separate unload or load too. In a group the button is active if at least one launcher is free; `Recharge` skips the busy ones. The tooltip of the grey button is as before ("Nothing to reload"). Ignoring a repeated press stays as a safety net.

## Pause after an unloading; unloading and loading are separate blocks
- Unloading and loading are whole, independent blocks; they only wait for each other to end. The pause after an unloading is a block of its own (`_pause`, `StartPause` / `PauseEnd` / `PauseLeft`): it starts when a REAL unloading ends (`Box.Real`: something was on the rails), whatever the cause (Missile button, Reload button, time limit), and only holds the next load back (half of `_magazineReloadTime`).
- Reload on empty rails: no unloading, no pause of its own, `Charge` at once (it still waits for a pause that is already running).
- Status: "Unloading rails" (animation) -> "Unloading: mm:ss" (the pause, whether a load follows or not) -> "Loading rails". After Missile OFF the pause runs, then "Rails empty".
- Colour / category: `SamBusy(ws)` (unloading, pause, loading) makes `Cat` = 2 (Reload, amber like "Reloading"); `SamStatus` is applied for categories 0 and 2.

## "Disable AI cheating" and Tight
`PlotHostilePostfix` also works on Tight when "On Tight: engage hostile aircraft" is on (`ShipsOnWeaponsTightEngageHostileAircraft.ResolvedValue`): only aircraft and helicopters marked hostile on the plot are added (the game lets them into the list on Tight only if their TRUE side is hostile: AI.cs ~4909, 4958). Hold: nothing. Reason: in a trace on Tight the target list was empty for a contact hostile on the plot.

## Default settings (from the user's cfg)
All values in the user's cfg already matched the defaults in the code except `[UI] SectionOrder`: the default is now `SAM,GUNS,ANTI-SHIP,ASW` (the other sections follow in the usual order). The keys `DebugFilter` and `AlertsClickThrough` in that cfg are leftovers without a description and are not set in the code.

## Sea Power 0.8.7 (beta 261026) — совместимость с 0.8.6 сохраняется (1.2.1)
* Репозиторий содержал устаревший дубль `src/Diagnostics.cs` (релизная DLL собрана без него, `AwcPlugin` вызывает `HookGunFixes`); с ним проект не собирается — файл надо удалить из репозитория.
* `WeaponSelector.GetWeaponSystemsForAmmunition`: в 0.8.7 есть перегрузка `(obj, pos, ammoId, target, List vwsList) : bool`, через неё идут почти все вызовы ИИ и HandleEngageTasks, а старая 4-параметровая вызывает её. Патчится ровно одна: новая, если есть (`SelectorPostfix5`, меняет vwsList на месте, при пустом списке result=false), иначе старая (`SelectorPostfix`). Общая логика — `SelectorCore`.
* `MissileSimulator.GetAccelerationTimes`: две перегрузки (AmmunitionParameters / in MissileParams) → `GetMethod` по типам аргументов (иначе AmbiguousMatchException).
* `WeaponSystem.targetIsInFiringArc` стал virtual; override в WeaponSystemCIWS вызывает base для надводных юнитов — патч базового метода достаточен, для самолётов CIWS (не вызывает base) не действует.
* Не проверено в игре: SAM warm-up (WeaponSystemLauncher.OnUpdate сильно изменён, `launch` теперь возвращает bool).


## Заметки по итогам теста 1.2.1 на 0.8.7 (для продолжения работы над AWC)
* Проверено в игре на 0.8.7: вспышка при пуске — ок; переключатели вкладки Weapons — вроде ок (проверка: выключить группу у юнита ИИ, смотреть трейс `ai targets …` / `pinned selector …`; в логе не должно быть «Weapons tab: N AI / save hook(s) not found»); SAM warm-up работает.
* **Исправлено:** Fire в карточке боеприпаса (`AmmoCard`, WeaponsView.cs ~1360) для группы из ОДНОЙ установки передавал `pin = null` → «any launcher»; при пустой/холодной установке игра отдавала заказ заряженной другой (трейс: `Fire clicked … launcher none (any)`). Теперь `scope.Count == 1 ? scope[0] : null`. Подтверждено пользователем. Не проверено: что делает закреплённый заказ к ПУСТОЙ установке (правильно — зарядить её и стрелять из неё, не перекладывать на другую).
* **SAM: «одну перезарядило, вторую нет» — разобрано, это не заказы.** Гипотезы «остаточный заказ в `_currentEngageTasks`» и «память цели» проверены и ОТКЛОНЕНЫ (память цели откатана). Код игры: заказ принимается установкой по одной ракете (`_shotsToFire--`, на последней удаляется из списка) — после последнего пуска его там нет; Salvo 2 = один заказ (uid, shots=1 в списке и у установки `_takenEngageTask`). Трейс `sam orders (launch: rails empty)` везде `list=0 … needHot=False`, а `box(pending=True)` появляется сразу после опустошения → источник — флаг `Box.Btn` (кнопка готовности ON / Reload): `LaunchPostfix` переносит его в новый box, `Demand` при `Btn` всегда «да» (до конца лимита SamMinutes или выключения кнопки). Асимметрия 14:43 (#0 холодная, #1 перезарядилась) — у #1 `Btn` был (её перед этим выгрузили/зарядили кнопкой), у #0 нет.
* **Открытый вопрос (решение за пользователем):** должен ли `Btn` переживать последнюю ракету («они все время перезаряжаются» в тесте 16:10 — установки были заряжены кнопкой/автозарядом). Варианты: оставить; сбрасывать `Btn` после последней ракеты; перезаряжать только по реальной потребности. Нужно уточнить, нажимал ли пользователь кнопки готовности перед тестом. Для проверки добавить `Btn` в строку `sam load` и трейс нажатия кнопки.
* Добавлены отладочные трейсы: `sam orders (state change | launch: rails empty)` (заказы корабля по боеприпасу, взятый заказ, итог `NeedHot`), `fire menu item: <пункт>`. Недостаток трейса: `Fire clicked` пишет только основная кнопка, пункт подменю — теперь отдельной строкой.
* Шум в трейсе: `pinned selector …` пишется ~80 раз в секунду на каждый Fire по закреплённой установке (новые вызовы селектора 0.8.7) — стоит ограничить (дедуп по uid заказа / раз в N мс).
* `sam demand: aircraft` пишется раз в секунду на каждую установку; при желании ограничить.
* Strict loadouts в Task Force Builder — это Smart Windows, не AWC (ведётся в чате SW).

## 27.10: SAM reloading after the last missile; trace changes (not released yet, version still 1.2.1)
- Decision (user): the readiness button (`Box.Btn`, also set by the Reload button) no longer keeps a launcher reloading after its last missile. `LaunchPostfix` drops `Btn` before asking `Demand`; `WarmPostfix` drops it when the rails are empty and no load is asked for (not Pending / Unload / engaging). After that the rails are loaded again only for a real demand: `NeedHot` (orders with a live target, the game's own preparation, the game's possible-target list with an air unit, an unidentified aircraft closing in) or the auto-charge (`AutoChargePostfix` calls `Charge` itself every 3 s while an unidentified aircraft closes in). The button still holds the missiles on the rails while they are loaded.
- The user did NOT press the readiness / Reload buttons before the 16:10 test ("they reload all the time"), so `Btn` was not the cause there. The real source of the `Pending` right after the rails emptied is still unknown. New debug lines show it at once: `sam charge <unit> <system> #n src=<launch: demand | count on empty rails: demand | after unload | button | Reload button | auto-charge | hot with empty rails: demand>`, and `sam load ... box(... btn=<bool>)`.
- Button traces: `sam button: ready ON | ready OFF | ready OFF ignored, engaging | Reload (unload, then load | rails empty: load) | Reload ignored (engaging | busy) | ready ON dropped (the last missile is gone | rails empty)`.
- Trace noise: `TraceLimited(key, sec, msg)` (AwcPlugin.cs, real time). `pinned selector ...`: the same line (per unit) once per 10 s, a changed offer is written at once. `sam demand: aircraft ...`: once per 10 s per ship and aircraft.
- Pinned order to an empty / cold launcher (checked in the code, 0.8.6 `WeaponSelector`): the game offers a launcher whose rails are empty if its magazine has the ammunition (`isWeaponSystemOutOfAmmo` looks at the loaded AND magazine counts); `SelectorCore` keeps only the pinned launcher, so the task is never given to another one. The cold launcher then runs the game's own order flow (`IsHot=true; StartReload()`, our `triggerLoadSystem` during the count: the load animation opens the reload) and fires. With no ammunition at all (magazine 0) the game drops the launcher and the order is discarded ("no systems found"): right. Not verified in the 0.8.7 game.
- Open (found while checking, NOT changed): with several launchers for the ammunition the game keeps only those whose preferred arc holds the target (then those with the target in the firing arc), so a pinned launcher that is not in that subset is not offered; `SelectorCore` then empties the list and the game discards the order ("pinned selector ... the pinned one is NOT among them"). A possible fix: put the pin back when it passes the game's own filters (operable, online, has ammunition, target in its firing arc if `_engageOnlyInsideArc`). Waiting for a trace that shows the case.

## 28.10: Fire zones in 3D are red
- The map wedges were NOT red in game (the earlier resource replacement had no effect; the cause was never traced). Zones.cs ArcColor now changes the theme brush Map_FiringArc IN PLACE (SolidColorBrush.Color, originals remembered and restored when the zones end; replacing the brush stays as the fallback; trace lines "arc colour: ..." under Debug). The 3D view uses WeaponArc3DRenderer.ARC_COLOR (static readonly, (1, 0.62, 0.1, 0.28), orange-yellow, written into the mesh vertex colours at Show). Zones.cs Arc3DColor: while the zones of the mod's Fire are shown the field holds red (0.92, 0.18, 0.18, 0.32), set before SetCandidate; the original value is restored when the zones end. Reflection on a static readonly field (Mono allows it); failure is traced only. Follows FireShowsZones. Not verified in game.

## 28.10: "Empty" status for non-gun launchers
- WeaponsView.StatusParts (given the Launcher): a non-gun launcher (not Gun/CIWS) in category ready/reload, not engaging, with Stock() == 0 (rails + magazine) shows "Empty" (`st.empty`). The game itself sets `_isEmpty` only for guns, chaff and hardpoints, so a missile launcher out of missiles kept saying "Ready"/"Reloading". Added to ru.json and langs/*.txt after `st.railsEmpty`. Not verified in game.
- Empty status is now a category too: Cat(ws, launcher) returns 4 (off/grey, the colour of disabled) for a non-gun launcher with Stock() == 0 that is not engaging; StatusParts shows "Empty" for it (only when the game does not already say Offline / Empty).

## 28.10: Fire zones, map colour: log analysis
- Fire zones, map colour (28.10 log): `zones: start` was reached but ArcColorOnce printed nothing - the loop over the first MapBase's MergedDictionaries found no brush silently. Now: all MapBase of the root are collected once (kept in `_maps`), each painted through `map.TryFindResource("Map_FiringArc")` first and through its merged dictionaries second (brush Color in place, originals restored when the zones end); every outcome is traced (`arc colour: maps found N`, `... -> brush colour set`, `nothing painted`). Wedge fill (weak red) NOT done: the template gives MapPie a Stroke only; waiting for this log.
- Fire zones, map colour, 2nd log: 1 MapBase found, `TryFindResource("Map_FiringArc")` = no resource, the map's one merged dictionary has no such key - the theme brush is NOT reachable that way (it is dropped as a resource approach). Now `ArcPaintStep` (Update; armed by ArcColor(true) at zones start; <= 60 tries every 0.1 s, then <= 10 more after the first success): walks the MapBase subtree, finds MapPie whose DataContext is WeaponArcSegmentViewModel and sets local Stroke (red 0xDD) and Fill (weak red 0x30) by reflection. Local values override the DynamicResource; nothing to restore (pies die with the zones). Tree walk is bounded and only runs for ~1-6 s after a Fire click, not in a loop of the game. Wedge fill included (user request 28.10). Not verified in game.
- Fire zones, map colour, 3rd log: armed, `maps found 1`, but no wedge was found inside the MapBase subtree within ~4 s (no exception). ArcPaintStep now walks the WHOLE tree of the status view's root (the wedges may be in a gizmo layer outside MapBase) and, if nothing is painted, traces once at ~0.5 s and once at ~3 s: `tree of N nodes: GizmoHost a, MapEllipse b, MapPie c [DataContext types]` - the next step depends on that line.
- Fire zones, map colour, 4th log: in the first visible status view's root: 3166 nodes, GizmoHost 106, MapEllipse 6, MapPie 0 (neither at 0.4 s nor 2.5 s after the start). Sources: MapPie is added to GizmoLayer.Children by GizmoHost.UpdateHost (Loaded), DataContext = WeaponArcSegmentViewModel, so the wedges are in the visual tree of the map's root - an earlier version of the mod found them by scanning "all visible roots". So the scan now goes through every distinct root of the visible status views. MapPie.OnRender draws `DrawGeometry(null, Pen, _geom)`: no Fill property, a map fill needs a patch of OnRender (not done). The 3D wedge is a filled mesh (Sprites/Default, vertex colour ARC_COLOR alpha 0.28, radius 6 units).
- Fire zones, 5th log: all roots (1), 3136 nodes, GizmoHost 106, MapEllipse 6, MapPie 0 again, at 0.4 s and 3 s after the start. Conclusion: no MapPie wedge exists in the 2D map tree while our zones are shown, so the yellow/orange wedges the user sees are the 3D mesh (WeaponArc3DRenderer, Sprites/Default, vertex colour ARC_COLOR). Setting the static readonly ARC_COLOR had no visible effect (no exception traced). Arc3DTint: the material `_material` (private static) gets `color` (1, .12, .12, 1.4) after each Show from ZoneReconcile (vertex colour x material tint) and white when the zones end. The 2D ArcPaintStep stays (harmless, bounded) in case a wedge appears in the map; remove it if the 2D map never shows wedges. Not verified in game.
- 3D wedge recolour (ARC_COLOR field, material tint) REMOVED at the user's request: the 3D wedges stay as the game draws them. The problem is only the 2D map (Tactical Display window). Smart Windows (1.6.1) was read: it only reorders windows (ZIndex), it does not touch the map. ArcPaintStep (2D) stays; its diagnostic now also lists every element type of the tree and every element with an arc/weapon/range DataContext (`arc colour: element types: ...`), because 0 MapPie in the tree contradicts the yellow wedges seen on the 2D map - the 0.8.7 template may draw them with another element.
- Fire zones, 6th log (28.10, 0.8.7): the 2D wedge is NOT a MapPie: `Canvas<WeaponArcSegmentViewModel=2` and `ContentPresenter<WeaponArcSegmentViewModel=2` (two arcs of the 76 mm) with no Path/Ellipse child inheriting that DataContext. `PaintWedge` now takes the Canvas with that DataContext and paints every shape inside (local red Stroke; weak red Fill where a Fill is set); if the canvas has no shape the trace lists its elements (type/DataContext). The 0.8.6-era sources in /home/claude/gsrc differ from 0.8.7 here.
- BUG found in the same log: with two unit windows (a second unit's status view open) `zones: start` repeated every frame (~25 ms): the other unit's CheckTargeting called ZonesUpdate(id = null) and ended the zones of the unit that was targeting, which started them again - the zones flickered and the colour step re-armed. Now the zones remember their unit (`_zoneUnit`): only that unit's view ends them, and a start is skipped only for the same unit + id + pin.

## 28.10 — 2D map firing-arc colour on Sea Power 0.8.7
- 0.8.7 no longer draws the wedge as a MapPie with a theme brush. `WeaponArcSegmentViewModel.Render(MapRenderContext)` opens a `ctx.Pie(128)` slot with `ctx.Palette.Stroke(MapColor.FiringArc, thickness)`; the template is an empty `<Canvas/>`, so the UI tree has no shapes (earlier tree-painting attempts could never work).
- Fix: while our Fire zones are shown, `MapPalette.Override(MapColor.FiringArc, red, red)` on the palette(s) of MapBase elements (found once, cached); restored when the zones end.
- Weak red fill: lazy Harmony postfix on `WeaponArcSegmentViewModel.Render` adds a second Pie slot (same geometry, `MapFill.From`), active only while `_zoneInfo != null`. All reflective, try/catch, skipped if the class is missing (0.8.6).
- Removed ArcPaintStep/PaintWedge diagnostics. 3D arcs are untouched.
- 3D wedges (user request, 0.8.7 confirmed the 2D colour works): postfix on `WeaponArc3DRenderer.Show` sets the mesh vertex colours (colors32) of `_arcObjects` to the map fill colour (235,45,45,a=0x30), only while `_zoneInfo != null`.

## 28.10: performance pass (no behaviour change)
- `R` reflection caches are now type -> name -> member dictionaries: no `FullName + "#" + name` string per call (CIWS update, CheckTargeting, HostStep run these every frame).
- `WC.B(color)` returns one shared SolidColorBrush per colour (nothing mutates a brush after it is set); the chevron's RotateTransform(90) is a single shared instance and assigned only on change. The tab refresh (every 0.3 s) no longer creates native Noesis objects for them.
- Trace arguments of the per-frame zone traces are built only when Debug is on.
- Not done (minor): StatusViewsStep list reuse, Trace file writer buffering, ArcFillPostfix delegate caching, HeaderStep give-up counter.

## 30.10: Special effects moved to Alternative Special Effects (1.3.0)
- Removed from this mod (code, cfg, window rows, texts in 9 languages, docs): Flash.cs, Optics.cs and the effect part of GunFixes.cs. Options: Effects/MuzzleFlashLight, HitFlashLight, MissileLaunchFlashLight, FlashPower, FlashLimit, DayBrightness, FireFlicker, FireLightPower; Optics/BinocularsNormalView, NormalViewDayOnly; Guns/MachineGunBurstFix, MachineGunSoundLoopFix, HitSoundFix. The new mod keeps the same [Section]/Key names and copies the values from this mod's .cfg on its first start; old keys in users' .cfg files are ignored by BepInEx, the .cfg is not touched.
- Kept: Guns/AimBeyondMaxRange, Guns/MaxAccuracyMeters (GunFixes.cs: AlignTurret, CIWS FireAtTarget, GetMeasurementRangeSigma patches), UI/FireShowsZones. Settings window: no "Visual effects" tab, no "Tracers and sound" group.
- No remaining users of the removed symbols outside the deleted files (checked by grep before deleting). Localization tools (ru.json, help.json, langs/*.txt, langs/help_*.txt) lost the same keys.

## 30.10: Shared settings hub (1.3.0)
- `src/SettingsHub.cs` (namespace `Rgreat.Hub`) is the same file in every rgreat mod, copied unchanged from Alternative Special Effects (canonical copy: its `src/SettingsHub.cs`); never edit it here. Protocol v1: a registry in AppDomain data `rgreat.settings.v1`; the first mod whose menu hook runs puts the button in (main menu under Mod Manager, pause menu under Options), the others find it.
- Registration at the end of `TryPatch` (AwcPlugin.cs), in its own try/catch: `SettingsHub.Register("alternative-weapons-control", () => Loc.T("menu.button"), PluginVersion, 20, anchor => SettingsWindow.Open(anchor as Noesis.UIElement, null), () => Enabled)` and `SettingsHub.Install(harmony, asm, Trace, () => Loc.Language)`. `Enabled` is the Mods-menu tick, asked each time.
- Text `menu.button` ("Weapons Control", ru "Управление оружием", other languages English) in Loc.cs, `Release/lang/*.ini`, tools (ru.json, langs/*.txt).
- `SettingsWindow.Open` needs only the cfg entries and the anchor's visual tree (top Grid/Canvas, else a Popup); no unit or mission object. Apply/OK are unchanged.

## 30.10: [Guns] AmmunitionFix - ammunition pack as a txt file
- Source: the user's `ammunition.fix.zip` (47 `#!extend ammunition/<name>.ini` files, keys `[Physics] CircularErrorProbable` and `[WarheadData] Power`; three files lack CEP, two have a broken `WarheadData]` line). Converted to `Release/ammunition_fix.txt` in the format of ASE's `hit_effects.txt`: `[ammunition file name]`, then `Key=value`. Not an ini of the game; the mod reads it itself (next to the dll).
- `src/AmmunitionFix.cs`: Harmony postfix of the `AmmunitionParameters(string, int, WeaponSystem)` constructor (also builds the shared masters that `Create()` clones). Sets `_circularErrorProbable = CEP * 0.0148809375` (projectiles only, as `loadProjectileParameters`) and `_warheadPower`; `_warheadDudPower` (x0.2, missiles x0.5) and `_ammoPoints` (= Power) are changed too only if they still hold the game's defaults, so an explicit DudPower / AmmoPoints in the ini stays. Option `[Guns] AmmunitionFix` (default true; Guns tab, group Ammunition), checked at each construction. Applied when the ammunition is loaded (mission start); switching the option does not rewrite already loaded ammunition. The encyclopedia reads the ini directly and still shows the original values.
- Keys in `Loc.cs`/tools: `grp.ammoFix`, `lbl.Guns/AmmunitionFix`, `help.Guns/AmmunitionFix` (9 languages).

## 30.10: Ammunition tab (SettingsAmmo.cs, AmmunitionFix.cs)
- New settings tab `tabs.ammo` "Ammunition": groups Accuracy (`Guns/MaxAccuracyMeters`, moved from the Guns tab), Ammunition (`Guns/AmmunitionFix`, moved), and the table (group `grp.ammoList`, drawn by `SettingsWindow.AmmoTable`, not by the generic `Row`). The Guns tab keeps `AimBeyondMaxRange` (group `grp.aiming`).
- Layers: the game's ini < the pack (`ammunition_fix.txt`) < the player's values (`AlternativeWeaponsControl.ammunition.txt` next to the .cfg, same format; a key is stored only when it differs from the pack, or from the game when the pack has none). `AmmoEffective` = player ?? pack; the constructor postfix applies it. Reset of a row / "All to game values" puts the game's values (stored explicitly when the pack differs); "Pack values" restores the pack's.
- The table works on the same work copy as the other settings: pseudo-keys `Ammo/<name>/0` (accuracy, m) and `/1` (power) are `Field`s created at the first opening (`EnsureAmmoFields`); `Saved` / `Default` / `Load` / `Commit` (`AmmoCommit`) handle them, so the yellow dots, Apply / OK / Cancel work as for cfg entries.
- Game values and names: `AmmoGameValues` reads the ammunition ini through `Utils.openIniFile("/ammunition", "/<name>.ini")` and `IniHandler.readValue` (float) - no `AmmunitionParameters` is constructed for the UI - and the name from `ammunition_names` (`LocalizationLoader.OpenVirtualLocalizationFile("language_<lang>", ...)`, first element of the "name,,type,description" line). Cached per name.
- Apply drops the shared masters of the changed ammunition from `AmmunitionParameters._sharedByType` (so the next load makes them again with the new values); weapons that already hold clones keep the old values until the mission is loaded again.
- New keys (9 languages): `tabs.ammo grp.aiming grp.ammoList ammo.note ammo.resetAll ammo.restorePack ammo.colName ammo.colCep ammo.colPower`.


## 30.10 (later): Guns tab merged, dependent settings hidden
- Tab `tabs.ammo` removed; its rows and the table now live in `tabs.guns`. Labels: "Guns: change maximum accuracy" (hint "game: ~40", `Field.Game` text other than "game: off" is shown via `set.gameVal`), "Guns: alter accuracy and power", "SAM readiness limit".
- `Field.Parent` (last argument of `Add`): a setting is not drawn while its switch is off (`ParentOn`). Pairs: UI/SamReadyButton -> UI/SamReadyMinutes, Ships/SamAutoCharge -> Ships/SamAutoChargeRange, Guns/AmmunitionFix -> the ammunition table. A switch click calls `DepsChanged`, which sets `_reshow`; `Tick()` redraws the body 2 frames later (not inside the click of the replaced checkbox).
- GunOneDirector and GunChannelLimit affect guns only (`WeaponSystemGun`, weapon type "Gun"); CIWS and machine guns are not touched.
