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
`_info.ini` ApproximateVersion=0.8.4 (все используемые члены игры проверены по декомпиляции 0.8.4 и 0.8.5).
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
  торпеда / RBU / ASROC → ASW, только удар по суше → LAND ATTACK); место — позиция `_mountObject` в осях корабля
  (fore/aft по z, port/starboard по x, если |x| > 25% максимального). Группа = раздел + имя + набор боеприпасов (×N).
* Домены: AIR (IsAirUnit), MSL (WeaponBase кроме торпед), SUR (Vessel, всплывшая ПЛ), SUB (погружённая ПЛ, торпеда,
  биология), LND (LandUnit). У пусковой — объединение по её боеприпасам: AAW → AIR+MSL, ASuW → SUR, ASW → SUB,
  `_landAttack` → LND, `_antiTorpedo` → SUB.
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

## Версия
* 1.0.0 (в работе): выделен из Smart Windows; шестерёнка и (i) на кнопке вкладки.
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе.
