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

## Орудия: опции [Guns] (GunFixes.cs)
* Пулемёты (14.5mm_2M-5 и др.) — Type=CIWS (`CIWS.cs`), не GunBarrel. Видимые трассеры — системы частиц эффекта
  (`CIWS._tracers`, дочерняя «Tracers» с rateOverTime = FireRate/60/5) с duration 1.1 с и loop=false: через 1.1 с после
  `startFire()` эмиссия прекращалась, очередь «через раз». MachineGunBurstFix: postfix `CIWS.startFire` — `main.loop = true`
  у всех играющих систем эффекта (белые и синие трассеры идут вместе). Emit(1) в начале очереди давал только синий — убран.
* Звук попаданий (HitSoundFix): у префабов `missile_hit_ship_small`, `_2`, `_4` (+ `missile_hit_air*`, `Bomb_cluster` и др.)
  нет AudioSource; postfix `EffectsManager.spawnEffect` внутри `Projectile.ProcessCollision` добавляет AudioSource с клипом
  explosion*/flak* «донора» (из `_effectsDictionary`), maxDistance >= 50 юн. Приоритет звуков не меняется. В сцене играет
  60-70 источников при 32 real voices (virtual 512).
* Звук пулемёта: пауза между очередями на слух — реальная пауза в стрельбе (дисциплина огня), а не ошибка; визуально трассеры
  цельные только потому, что летят до 12 с. Звук гаснет через 0.3 с после последнего выстрела (игра) — оставлен как есть.
  Пробовали продлевать звук через паузу — отвергнуто: звук должен идти только пока пулемёт стреляет.
* Trace: звук очереди — клип `tripleA` 2.16 с, loop=true, playing/virtual False/громкость 1 всю очередь (3 с) → пауза
  в середине = тихая часть самого клипа при зацикливании (позиция 1.98 → 0.03). MachineGunSoundLoopFix: громкость клипа
  меряется окнами 50 мс (`AudioClip.GetData`), позиция воспроизведения перепрыгивает с конца громкой части на её начало
  (postfix `CIWS.update`). Если клип не читается (compressed) — ничего не делает, пишет в trace. 
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

## Вспышки света (Flash.cs, [Effects] MuzzleFlashLight/HitFlashLight=false, FlashPower=1) - пункт 6, СДЕЛАН (не проверено в игре)
- Дульная вспышка: postfix `GunBarrel.LaunchProjectile` -> точечный Light в `_gunObject.TransformPoint(_effectPosition)`; CIWS/пулемёты: в `CiwsUpdate` пока `_isFiring`, ~8 вспышек/с у `_tracers`.
- Попадание: в `HitEffect` (spawnEffect во время `Projectile.ProcessCollision`), пропуск эффектов с water/splash/wake в имени.
- Light живёт `AwcFlash` (MonoBehaviour: затухание, самоудаление); не более 10 одновременно, камера дальше ~10 nm - пропуск. 1 unit = 67 м.

## Обычный вид в бинокле на макс. увеличении (Optics.cs, [Optics] BinocularsNormalView=true, NormalViewDayOnly=true (по умолчанию)) - пункт 7, СДЕЛАН (не проверено в игре)
- `CameraManager` на профиль бинокля (`binocular_7x50`, `bridge_binocular_20x120_stab`) вешает `FullscreenShaderToggleCycle.Configure(...)`: фильтр (ночной/тепловой) включён сразу (`effectEnabled=true`), `toggleKey=None`.
- Postfix `CameraManager.ApplyOpticalPostProcessProfile`: если режим Binoculars, профиль последний в `_availableOpticalViewProfiles` (макс. увеличение), есть материалы фильтра, (опция DayOnly: `DynamicSky.getSunlightIntensity() >= 0.5`) -> `effectEnabled=false`, `toggleKey` = исходный (`originalToggleKey`, KeypadDivide) - игровая клавиша включает фильтр обратно.
- Профили `optical_views/*.ini` в распакованных файлах не нашёл (внутри игры) - какие именно фильтры у профилей, не проверено.

## Правка: вспышки и огонь
- Вспышка попадания теперь 0.28 с (было 0.14).
- Мерцание пожара ([Effects] FireFlicker=true, Flash.cs `AwcFireFlicker`): postfix `Compartments.CreateFireEffect` вешает компонент на игровой `pointlight` эффекта пожара; модуляция intensity/range шумом Перлина, глубина и частота зависят от силы (small 0.5 / moderate 1 / severe 1.6 - по сравнению effect с `_obp._small/_moderate/_severeFireEffect`). Базовые значения света берутся из префаба игры. Не проверено в игре.

## Свет при пуске ракет ([Effects] MissileLaunchFlashLight=false, Flash.cs)
- Prefix/postfix `Missile.ScheduleFlightEffects` (только первый вызов: `_flightEffects==null` до вызова) -> Light, прикреплённый к ракете (идёт за ней), живёт `MissileSimulator.GetAccelerationTimes(ap, airLaunched).primaryTime` (время работы стартового ускорителя; максимум 20 с), яркость держится 70% времени, затем гаснет.
- Время есть у всех ракет: для Kinematics=Full это `AccelerationTime` (в ini задан у 96 из 163 ракет, иначе по умолчанию 6 с), для остальных считается из max velocity / ускорения. `BoosterEffect` (дым/пламя) нет только у pla_yj-6 и usn_aim-54a - свет у них всё равно будет. Лимит одновременных огней для ракет: 18.

## Правка: ярче взрывы и пожары
- Вспышка попадания: пик 8 (было 4), 0.56 с (было 0.28), радиус 10.
- Пожары: свет НЕ наш - игровой `pointlight` эффекта пожара отсека (`Compartments.CreateFireEffect`: позиция = `_obp._compartmentEffectPositions[i] + effect._position`). Мерцание его модулирует; добавлен [Effects] FireLightPower=3 (множитель яркости, радиус растёт на 50% множителя-1), минимумы базы 0.5 / 3.

## Вспышки на все взрывы ([Effects] HitFlashLight)
- Postfix `EffectsManager.spawnEffect(ep)` (`HitEffect(__result, ep)`): `ExplosionFlash` смотрит `ep._effect._effectClassName/_name`: содержит explosion / impact_ / blast / detonat -> вспышка; water / splash / underwater / wake / starshell исключены. Размер по Tiny/Small/Medium/Large/VeryLarge (0.4..2.4), flak x0.7; яркость 8*sqrt(k)*1.2, радиус 10*k, время 0.56*(0.6+0.4k). Не только снаряды орудий, но и ракеты, боевые части, бомбы, взрывы боезапаса.

## Яркость применяется сразу
- `AwcFlash` каждый кадр читает [Effects] FlashPower (`FlashK`) и пересчитывает range/intensity (хранит базовые Peak/Range); `AwcFireFlicker` уже читал FireLightPower каждый кадр. Окно настроек работает на копии - значения вступают в силу по Apply/OK, уже горящие огни и текущие вспышки меняются без перезапуска.

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

## Бинокль (Optics.cs): пользователь сообщил «всё ещё термальный вид при макс. увеличении»
- Причина по коду не найдена: `effectEnabled=false` в `FullscreenShaderToggleCycle` даёт обычное изображение (OnRenderImage), других мест включения нет. Добавлена трассировка (Debug) `optics ...`: профиль, режим, число профилей, последний ли, материалы/фильтры, DefaultFilter. Нужен лог с Debug. Исправлено: `toggleKey` - ПОЛЕ (SetF, а не SetP).

## Settings window tabs
SettingsWindow.cs: each Field has Tab + Group. Tabs: Weapons panel (Panel, SAM readiness button), Fire Control (SAM, Fire control), AI (On Free: unidentified aircraft, Chasing), Guns (Accuracy, Tracers and sound), Visual effects (Binoculars, Flashes, Fires), Debug. Tab bar is row 1 of the window; a yellow dot on a tab = unapplied changes inside it. Current tab is kept while the game runs.

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
- Defaults changed to the user's cfg: Muzzle/MissileLaunch/HitFlashLight=true, UnknownAirMaxNm=20, SamAutoChargeRange=30, ClickVolume=0.5.
- Fire zones scan (Zones.cs ArcZStep): every frame for 90 frames after the zones start, then 4/s; all visible roots; hides AttackingState max-range ellipse, wedges red + Z 20 re-applied on every scan (they are rebuilt when the hovered weapon changes).
- Fire zones: Harmony postfix on AttackingState ctor starts a per-frame scan (120 frames) that collapses the max-range circle; the wedges' orange is replaced by changing the colour of the game's resource Map_FiringArc (ArcColorStep, every 1 s, found via FindResource); the per-wedge Stroke is only a fallback if that fails.
- Fire zones, range circle (replaces the scan): Harmony prefix on AttackingState.set_MaxRangeInLat stores the real value (ConditionalWeakTable) and passes 0 (FireShowsZones on) - the MapEllipse never gets a radius, the max label hides with it; prefix/postfix of UpdateBearingPreview put the real value into _maxRangeInLat for the call (line ~742 uses it). The scan of ArcZStep now only sets the wedges' Z-index 20.
- Fire zones, FINAL approach (replaces the zero radius, resource colour and the scan): Harmony postfix ZoneStylePost on MapEllipse.MarkDirty, MapPie.MarkDirty and MapPath.StrokeChanged (property-changed callbacks, so it runs when the control gets its values): MapEllipse of AttackingState with SemiMajorAxis == MaxRangeInLat -> local Stroke fully transparent; MapPie of WeaponArcSegmentViewModel -> local red Stroke + Z-index 20. A local value overrides the DynamicResource brush; the shared resources and the model stay untouched.
- Fire zones, CURRENT: the transparent-stroke patches of MapEllipse/MapPie/MapPath callbacks CRASHED the game at start - reverted to the zero-radius circle (AttackingState.set_MaxRangeInLat prefix + UpdateBearingPreview swap) and the UI-tree scan for the wedges (red Stroke always, Z 20, Map_FiringArc colour changed too). Do not patch the map controls.
- Fire zones, FINAL (no scanning): zero-radius max-range circle (set_MaxRangeInLat prefix + UpdateBearingPreview swap) and red wedges via Harmony postfix of ThemeDictionarySwapper.ApplyTheme, which replaces Map_FiringArc in the dictionary just merged into the map (the theme is local to the map, so FindResource from the status view never saw it). The UI-tree scan and the wedges' Z-index step are gone (the circle was what covered them). Takes effect when the map loads / theme changes, so FireShowsZones is read then. Patching MapEllipse/MapPie callbacks crashed the game at start.
- The game crashed at start with ThemeDictionarySwapper.ApplyTheme postfix changing the dictionary inside the callback: now the postfix only notes the swapper, ThemeSwapStep (Update) changes Map_FiringArc on a later frame. If it still crashes - suspects: that patch, then set_MaxRangeInLat/UpdateBearingPreview.
- LogOutput of the crash ended right after 'Enabled in the game's Mods menu' (patching at start): the ThemeDictionarySwapper.ApplyTheme patch is REMOVED (do not patch it). Wedge colour now: ArcColorOnce at the start of the zones finds the MapBase once (cached), replaces Map_FiringArc in its MergedDictionaries by a red brush.
- Missile booster light scales with the missile mass (ap._massInKg, ln(mass+250) scale, 15..7000 kg): brightness 25..100 %, radius 50..100 % (Flash.cs MissileLaunchFlash).
- Muzzle flash and hit flash graded like the missile booster light (brightness 25..100 %; radius muzzle 50..300 m, hit 100..600 m, missile 50..100 % of 12 units): muzzle by the shell mass (ammo._ap._massInKg, ln(mass+10), 0.13..862 kg; BarrelFlash got the 'ammo' argument), hits by the size tier in the effect name (Tiny 0 .. VeryLarge 1; 100 % = 24 range / 14.9 peak). CIWS/machine-gun flashes unchanged.
- CIWS/machine-gun flash radius: shot 50 m, hit 100 m (hit by effect impact_small_shell_multi = 0.5 in..35 mm shells); brightness unchanged.
- Flash of torpedo / bomb / RBU detonation: postfix of WeaponBase.CreateDesctructionEffects (prefix sets _inDetonation, which turns the generic ExplosionFlash off for these three types - one flash, not two); strength = ap._warheadPower on the guns' Power scale (0.1..35, capped), hit scale: brightness 25..100 %, radius 100..600 m; duds (_warheadMalfunction) none; the light is lifted to y>=0.3. ASROC / depth charges (AirDepthCharge) are not covered.
- Flash duration also graded by the same strength: 100..300 % (muzzle 0.09..0.27 s; hits and detonations 0.39..1.18 s; autocannon/MG hits and CIWS shots stay at 100 %).
- The max-range circle is hidden only for a targeting state started by the mod's Fire button / its menu (OurFireExecute wraps the command, OurFireDepth > 0 when the state first gets MaxRangeInLat; marked in RealMax). The game's own Fire keeps its circle.
- Zones (wedges) are shown only for targeting started by the mod's Fire (IsOurState, the RealMax mark); the game's own Fire shows neither the zones nor hides its circle.
