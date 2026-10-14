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
  под мышью, точка «отличается от умолчания», ↺, Defaults / Cancel / Apply / OK; работает на копии, Apply пишет .cfg.
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
* Вкладка (kind 6) вставляется в `Tabs` второй. Доступна для своих Vessel / Submarine с пусковыми, пока мод включён.
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
  выключить игроку. Выключить: Off; если все пары пункта выключены — пункт → No, их Off сброшены. Смена приказа
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
  all. Заголовки колонок, разделы-карточки.
* Колонки: имя | STATE 190 px (обрезается по ширине) | AMMO 104 px | FIRE 128 px (слоты 64/28/28, пустые сохраняют место) |
  чипы доменов 34 px. FIRE + чипы — «блок управления»: полупрозрачная голубая полоса `Band` во всех строках, линия слева.
* Строка группы (2 линии): имя пусковой (ссылка → справка системы: `GunMuseumEntries` / `LauncherMuseumEntries` по
  `_systemName`, окно `ObjectInformationViewModel(MainGameView.Instance, entry)` через `WindowHostCommands.OpenWindowCommand`;
  нет записи — справка первого боеприпаса) ×N, в конце первой линии — счётчик каналов (см. ниже); второй линией: один
  боеприпас — имя (ссылка → `WeaponEntry.OpenReferenceCommand`, описание в подсказке), прозвище, `_displayedType`,
  характеристики — отдельные TextBlock с подсказками (наведение, дальность, скорость, «pwr» = `WarheadData.Power`),
  ClipToBounds; несколько — их имена-ссылки. Ссылки подчёркиваются под мышью. Шеврон — Path-треугольник (90° при раскрытии).
* Раскрытие (если есть что показать): строки боеприпасов (если их несколько: рамка с описанием, свой запас, Fire) и
  пусковые группы по одной (метка | состояние | запас | Fire + ■ + ↻ | чипы). Первое открытие для юнита: всё раскрыто,
  после разметки — если не влезает (ExtentHeight > ViewportHeight), всё свёрнуто; раскрытие запоминается по юниту на
  сессию (`OpenGroups`). Обновление раз в 0.3 с, пока вкладка видна.
* Состояние: сводка по `Cat` (Inoperable / offline-empty / reload / engaging / ready / 5 waiting; у одиночной —
  `GetStatus()`, полный текст в подсказке). Пока у пусковой есть цель (стрельба, наведение, перезарядка в залпе; у CIWS —
  `_currentClosestTarget`) — «→ цель» как её видит игрок (`GetMapVehicle(Globals._playerTaskforce)`: «Track N», опознанная —
  с именем; своя — «Track N (имя)» по `GetMapContactID()`); длинное — на 2-ю строку.
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
* Привязанная задача (`PinRec`, AttackExecPostfix; проверка в HetLeave, `UpdatePins`): ждёт — «Queued» (пусковая занята)
  или «Waiting for FCR channel» (`NoChannel`); `Cat` 5 «waiting» (жёлтое) с целью. Ждёт до первого выстрела (заряжено
  меньше, чем при привязке, или `_engageState` LaunchingSalvo/Launch/Firing); снять — ■.
* Орудия/CIWS не ждут: `WeaponSystemGun.SetTarget` пробует `TryToAddTarget` директоров, неудача не мешает; точность по
  `GetFireControlSensor()` = лучший включённый исправный директор (`FireControl.ResolveBestDirector`, каналы не смотрит)
  с его `_fcSolutionGainRate`; без директора — местное управление `Globals._gunLocalControlGainRate` 0.6 и ошибка
  ×(1+2·темнота). Состояние « · local control» — если директоры есть, но ни один не работает (`LocalControl`: не
  Inoperable, IsOn, CanProduceContacts); без директоров вовсе штрафа нет — не помечается.
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

## Версия
* 1.0.0 (в работе): выделен из Smart Windows; шестерёнка и (i) на кнопке вкладки.
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе.
