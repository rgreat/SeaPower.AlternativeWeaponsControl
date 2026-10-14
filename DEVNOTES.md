# Alternative Weapons Control — заметки для продолжения работы (для Claude)

Мод для Sea Power: вкладка Weapons в окне Unit Status (своих кораблей и подводных лодок) — пусковые, цели, боезапас,
каналы управления огнём, автоматическое применение по пусковым и доменам целей, огонь / прекращение / перезарядка;
поправки к управлению огнём орудий. BepInEx 5 + Harmony, загружается через Anchor Chain (Workshop 3380210757). Устроен как
Alternative Damage Model: настройки — шестерёнка на кнопке вкладки, легенда — (i) рядом. Выделен из Smart Windows
(репозиторий rgreat/SeaPower.SmartWindows, там вкладка была 1.6.0 в разработке).
Доступ к игре — через рефлексию (класс `R` в WeaponsModel.cs: поля/свойства любой видимости, методы по имени с подбором
перегрузки, кэш); Noesis — напрямую (ссылка на Noesis.NoesisGUI в csproj).

## Сборка в песочнице
Как у Smart Windows (см. её DEVNOTES: dotnet-sdk-8.0, GameRefs.zip пользователя → `/home/claude/game`, BepInEx 5.4.23.4,
Anchor Chain V1.1.0 dev.zip, пустой `nuget.config`). Скрипт `/home/claude/tools/build_awc.sh [repoDir] [zip]` — сборка +
упаковка `AlternativeWeaponsControl.zip` (Release/, src/, csproj, nuget.config, README*, PUBLISHING.md, DEVNOTES.md).
Сборка сама копирует dll в `Release/` (target StageRelease). Номер билда (`BuildStamp`) пишется в лог.

## Выдача результата (предпочтения пользователя)
Каждая сборка — zip БЕЗ корневой папки. Общение — по-русски, коротко; в .cfg — один переключатель на функцию. В отчётах не
повторять рутинное. Править исходники и коммитить — только по просьбе пользователя. Если правка ломает работу мода на
поддерживаемых версиях игры — предупредить заранее.

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
* Окна UnitStatusView — postfix конструктора (список слабых ссылок); каждый кадр (Update, при включённом моде) для видимых:
  кнопка вкладки и хост содержимого (`HostStep`): первый элемент с Visibility, привязанной к «…Tab.IsSelected», его
  Panel — туда Border (та же ячейка, Margin), Child = наш вид, виден, пока вкладка выбрана; `WeaponsTick` там же.
* Вместе со Smart Windows: её вкладка Reference — kind 5, наша — 6; хуки на одни и те же методы UnitStatusViewModel и свои
  хосты не мешают друг другу. Ссылки вкладки на установки открывают Unit Reference без дерева — Smart Windows (1.5.1+)
  считает такие окна окнами Weapon Reference.
* Выключен в меню Mods: вкладка недоступна (после смены юнита), хуки AI/огня ничего не меняют (`Enabled`).

## Настройки (.cfg)
[UI] ClickVolume (0.4); [FireControl] GunOneDirector (true), GunChannelLimit (false); [General] Debug (trace в
`BepInEx/AlternativeWeaponsControl.trace.txt`).

## Вкладка Weapons (`src/WeaponsModel.cs`, `WeaponsAi.cs`, `WeaponsView.cs`)
Перенесено из Smart Windows (там была 1.6.0 в разработке); история правок сохранена.
* Вкладка (kind 6) вставляется в `Tabs` второй (`AwcPlugin.cs`: ModTab, хост рядом с Grid'ами вкладок игры). Доступна
  для своих Vessel / Submarine с пусковыми, пока мод включён в меню Mods.
* Пусковые = `_obp._weaponSystems` (индекс в списке — ключ настроек) без Chaff / Noisemaker / Hardpoint / UNKNOWN; имя —
  `DisplayName.Value`; боеприпасы — `_associatedAmmunitionNames` (те, что есть у юнита, `getAmmunitionByName`); раздел по
  `_weaponType` (CIWS, Gun) и первому боеприпасу (AAW → SAM, ASuW → ANTI-SHIP, ASW / торпеда / RBU / ASROC → ASW, только
  удар по суше → LAND ATTACK); место — позиция `_mountObject` в осях корабля (fore/aft по z, port/starboard по x, если
  |x| > 25% максимального). Группа = раздел + имя + набор боеприпасов (×N).
* Домены: AIR (IsAirUnit), MSL (WeaponBase кроме торпед), SUR (Vessel, всплывшая ПЛ), SUB (погружённая ПЛ, торпеда,
  биология), LND (LandUnit). У пусковой — объединение по её боеприпасам: AAW → AIR+MSL, ASuW → SUR, ASW → SUB,
  `_landAttack` → LND, `_antiTorpedo` → SUB.
* Переключатели игрока: `UnitW.Off[индекс] = маска выключенных доменов` (ConditionalWeakTable по юниту), сохраняются в
  секцию юнита сохранения (`AlternativeWeaponsControl_WeaponAuto=idx:mask,...`, постфиксы ObjectBase.Save/LoadStateFromFile).
* Объединение со Standing Orders юнита (`TacticalDoctrine`): AutoAttackSurface (все пусковые × SUR/SUB/LND, кроме
  LandUnit), ShipsAutoUseSAMsAgainstSurfaceTargets (пусковые с ЗУР AAW+вторичная ASuW × SUR/SUB/LND, свои корабли),
  ShipsAutoUseAntiShipMissilesAgainstSurfaceTargets (пусковые с ракетами ASuW × SUR). Разрешено приказами, если не
  запрещено AutoAttackSurface и хотя бы один боеприпас пусковой для домена не запрещён своим пунктом. Чип: «·» нет
  домена, жёлтый — запрещено приказом, серый — выключено игроком, зелёный — авто; у группы разные — «mixed».
  Включить запрещённое приказом: пункт юнита → Yes (IsSet, Value), всё, что от этого включилось кроме нажатого, —
  выключить игроку. Выключить: Off; если все пары пункта выключены — пункт → No, их Off сброшены. Смена приказа
  извне (окно Standing Orders, наследование) — по снимку ResolvedValue (`SyncDoctrine`): пары пункта сбрасываются.
* AI (`WeaponsAi.cs`): «автоматически» = внутри AI.OnUpdate / OnFixedUpdate (счётчик), или внутри
  ObjectBase.HandleEngageTasks для задач с `_isAutoAttack` (все задачи по этим боеприпасу и цели — авто). Постфикс
  `WeaponSelector.GetWeaponSystemsForAmmunition` убирает выключенные для домена цели пусковые (список новый) — AI
  тогда и боеприпас не выбирает. CIWS: prefix `WeaponSystemCIWS.AquireTarget` — сбросить текущую цель запрещённого
  домена и собрать позиции запрещённых объектов (угрозы, Vehicles, LocalVehicles); postfix `targetIsInFiringArc` для
  этого CIWS — false на этих позициях. Ручная стрельба не фильтруется.
  Finalizer HandleEngageTasks: игра раздаёт задачу ещё всем орудиям / CIWS с тем же боеприпасом и связанным пусковым
  (мимо селектора) — авто-задача на пусковой, выключенной для домена цели, снимается (`EndEngageTask`).
  Ограничение CIWS: цель выбирается одна (ближайшая, ракеты раньше самолётов); запрещённая просто не видна в секторе.
* Вид (Noesis кодом): шапка (Free/Tight/Hold — `SetWeaponStatus(status, true)`, Hold ещё `CeaseFire(false)`; «On Tight:
  engage hostile aircraft» — пункт ShipsOnWeaponsTightEngageHostileAircraft юнита; Expand/Collapse all), заголовки
  колонок, разделы-карточки. Строка группы (2 линии): имя пусковой (ссылка → справка системы: `GunMuseumEntries` /
  `LauncherMuseumEntries` по `_systemName`, окно `ObjectInformationViewModel(MainGameView.Instance, entry)` через
  `WindowHostCommands.OpenWindowCommand`, как в Unit Reference; нет записи — справка первого боеприпаса) ×N; второй
  линией: один боеприпас — имя (ссылка → `WeaponEntry.OpenReferenceCommand`, описание в подсказке), прозвище,
  `_displayedType`, характеристики (MuseumWeaponEntry), справа «Fire ▾» (ContextMenu из `WeaponEntry.Items`, как у
  кнопки нижней панели); несколько — их имена-ссылки. Ссылки подчёркиваются под мышью. Сводка состояния по `Cat`
  (Inoperable / offline-empty / reload / engaging / ready; у одиночной — `GetStatus()`, полный текст в подсказке),
  запас = заряжено + магазины без повторов, чипы. Раскрывается, только если есть что показать: карточки боеприпасов
  (если их несколько) и пусковые группы по одной (метка | состояние на всю ширину до запаса | запас | чипы).
  Клик по чипу — звук кнопок игры: PlaySoundAction из Interaction-триггеров первой найденной кнопки (ButtonBase) в UI
  (поиск один раз, Uri в trace), `Noesis.GUI.PlayAudio`. Обновление раз в 0.3 с, пока вкладка видна. Первое открытие
  для юнита: всё раскрыто, после разметки — если не влезает (ExtentHeight > ViewportHeight), всё свёрнуто; раскрытие
  запоминается по юниту на сессию (`OpenGroups`).

* Правки (после проверки): колонка FIRE перед чипами — вместе с ними «блок управления» (полупрозрачная голубая
  полоса `Band` за этими колонками во всех строках, линия слева); Fire — split: «Fire» → `WeaponEntry.Command`
  (AttackingState.TryStart: курсор выбора цели, как кнопка нижней панели), «▾» → меню `WeaponEntry.Items`. Несколько
  боеприпасов — строки боеприпасов в раскрытой группе (рамка с описанием, свой запас, Fire в той же колонке). Шеврон —
  Path-треугольник (поворот 90° при раскрытии). «mixed» — левая половина круга залита зелёным, кольцо зелёное.
  БЧ в характеристиках — «warhead N» (`WarheadData.Power` боеприпаса). Имена установок и мест — с заглавной.
  Звук клика чипов: у кнопок игры звука нет — мод делает свой клип в коде (AudioClip.Create, тон 1.9 кГц + шум,
  45 мс), 2D AudioSource на объекте плагина, через рефлексию (UnityEngine.AudioModule не в ссылках);
  [UI] ClickVolume 0..1 (0 — без звука). «On Tight» — отметка 6×6 зелёная, рамка зелёная при «да».
* Ещё правки: щелчок у всех кнопок и переключателей вкладки (`Click(e, a, sound)`; ссылки — без звука). Fire: подсветка
  частей под мышью; пока игра в AttackingState этого юнита с этим боеприпасом (`Globals._mainGameViewModel.StateManager
  .CurrentState`, проверка каждый кадр, пока вкладка видна) — кнопка красная. Характеристики — отдельные TextBlock с
  подсказками (наведение, дальность, скорость, «pwr» = мощность БЧ), обрезка по ширине (ClipToBounds). Состояние:
  `GetStatus()`, пока у пусковой есть цель (стрельба, наведение, перезарядка в залпе; у CIWS — `_currentClosestTarget`) — «→ цель» как её видит игрок (`GetMapVehicle(Globals._playerTaskforce)`: «Track N»,
  опознанная — с именем; своя — DisplayNameShort); длинное переносится на 2-ю строку, по центру. Запас «текущий/макс»:
  макс = начальные запасы погребов (`_initialAmmoCount`, каждый погреб один раз) + `_maxAmmoCount` пусковых без
  погреба; у отдельных пусковых — заряжено/`_maxAmmoCount`; текущий >50% зелёный, >25% жёлтый, иначе красный; у орудий и
  CIWS с максимумом меньше 10 — одно число без цвета.
* Блок управления строки (колонка 128 px, слоты 64/28/28 — пустые сохраняют место): Fire (split), «■» прекратить огонь,
  «↻» перезарядка (только если у пусковых есть погреб, `hasAMagazine`; активна при `CanForceReload`). Где: строка группы
  (Fire — если боеприпас один; привязан к пусковой, если она одна), строки боеприпасов (только Fire), строки пусковых
  группы (своя Fire + ■ + ↻). Прекратить огонь: `EndEngageTask` у ведущих огонь (CIWS ещё `StopEngage`), из
  `_currentEngageTasks` юнита убираются ждущие задачи с этими боеприпасами — привязанные к этим пусковым, или все, если в
  области все пусковые юнита с этим боеприпасом (`RemoveEngageTask`). Активна, пока есть что прекращать.
* Цель отдельной пусковой (`WeaponsAi.cs`): Fire / ▾ пусковой ставит `Pending` (юнит, пусковая, её боеприпасы).
  Клик по цели создаёт приказ AttackTask в `_userRoot` сразу (и на паузе), а задачу EngageTask — только когда приказ
  выполняется: postfix `AttackingState.AddAttackTask` привязывает к пусковой новый (первый) приказ юнита (`OrderPins`),
  postfix `AttackTask.OnExecute` передаёт привязку созданной им задаче (последняя задача юнита с этим боеприпасом и
  целью, `Pins`). Так на паузе можно назначить несколько пусковых на разные цели (раньше привязка по времени миссии
  ломалась: на паузе оно стоит). Селектор при раздаче: у задачи с привязкой — только
  её пусковая (нет в списке — задача ждёт). Finalizer: другие системы, взявшие привязанную не к ним задачу (все орудия с
  тем же снарядом, связанные пусковые), её бросают. Pending живёт, пока у игры AttackingState этого юнита, и 3 с после
  (30 с, если выбор цели так и не начался). Красная подсветка — у Fire той пусковой. Ограничения: залп больше её запаса
  не перейдёт на другие; цель вне её сектора — выстрела не будет; приказы с нижней панели пусковую не выбирают.
  Игра отдаёт новую задачу ВСЕМ орудиям с тем же снарядом (и CIWS, связанным пусковым), затирая их текущую стрельбу:
  если у юнита есть привязанные задачи, prefix HandleEngageTasks запоминает поля стрельбы всех систем
  (`EngageFields`), finalizer тем, кому досталась чужая привязанная задача, возвращает прежнюю стрельбу (если она была),
  иначе EndEngageTask — так две пусковые стреляют по двум разным целям.
* Каналы управления огнём (директоры = `_vwp._associatedSensors`). Директор нужен, только если
  `ObjectBase.DoesEngageRequiresSensor(ap, цель)`: снаряды, SARH для этой цели, радиокомандная коррекция; пусковая без
  директоров — «free to fire». Ограничивают только радарные (`SensorSystemRadar`): нужен директор (не Inoperable, IsOn
  или `_autoEnableSensor`, `CanGuideAmmo`) с (свободным T или уже на этой цели `IsAimedAtTarget` / `_preLaunchTargetObject`)
  и свободным W (`_associatedWeapons.Count < EffectiveWeaponChannels`). Сонар / оптика каналами не ограничены.
  Игра: задача игрока без годной пусковой не снимается (только авто — по возрасту); ракетам корабля при занятых каналах
  отдаёт пусковую с `ignoreSensors` — пусковая берёт задачу и ждёт канал сама; пусковая занята — список пуст, и игра
  «breaks off» под приказ игрока чужую стрельбу (орудия; пусковые, если цель — ракета).
  Мод: в конце первой строки группы (после имени и ×N) рамка, как у типа оружия, — «T занято/всего · W занято/всего» по уникальным радарным директорам группы (боеприпас
  `_requiresGuidance` или Projectile; W — только для управляемых), жёлтым (текст и рамка), если T или W кончились; подсказка — по
  директорам. Проводные торпеды (`_midCourseCorrection.IsWireGuided`, `_maxWireCount` < MaxValue) — «Wires x/y».
  Привязанная задача (`PinRec`, заводится в AttackExecPostfix): ждёт — «Queued» (пусковая занята, задача в списке) или
  «Waiting for FCR channel» (пусковая взяла её, а канала нет, `NoChannel`); состояние `Cat` 5 «waiting» (жёлтое) с
  целью. Ждёт без ограничения времени, до первого выстрела (заряжено меньше, чем при привязке, или `_engageState`
  LaunchingSalvo/Launch/Firing); снять — ■. Орудия/CIWS не ждут никогда (`NoChannel` = false): игра наводит их и без
  свободного канала — `WeaponSystemGun.SetTarget` пробует `TryToAddTarget` директоров, неудача не мешает;
  точность по `GetFireControlSensor()` = лучший включённый исправный директор (`FireControl.ResolveBestDirector`, каналы
  не смотрит) с его `_fcSolutionGainRate`; без директора — местное управление `Globals._gunLocalControlGainRate` 0.6
  и ошибка ×(1+2·темнота). Т.е. штрафа за занятый канал в игре нет.
  Состояние орудия/CIWS с целью: « · local control», если директоры у него есть, но ни один не работает (`LocalControl`:
  не Inoperable, IsOn, CanProduceContacts — как ResolveBestDirector) — ровно когда игра даёт штраф. Без директоров вовсе
  штрафа нет (режим Director, gain 1) — не помечается.
  [FireControl] GunChannelLimit (false; окно настроек → Fire control): postfix
  `WeaponSystemGun.GetFireControlSensor` (только орудия; CIWS — свой `FireControl.Classify`, не трогаем): если орудие
  ведёт огонь по цели, а выбранный директор её не держит (`IsAimedAtTarget`), берётся другой работающий, который держит;
  нет — `TryToAddTarget` на каждом вызове (канал освободился — орудие его подхватит; `_isGuiding`, `_targetObject` как в
  activatePreLaunchGuidance); не вышло — null → местное управление (gain 0.6, ×темнота). Пометка «local control» в
  состоянии при включённой опции учитывает и это (рабочий директор, не держащий цель, не считается). `ref object
  __result` при ссылочном типе возврата Harmony принимает (IsAssignableFrom). Внимание: игра в `FreeSensors` (конец
  стрельбы орудия) очищает ВСЕ цели его директоров — остальные орудия перезахватят канал на следующем вызове.
  [FireControl] GunOneDirector (true; Fire control → «Guns take one director»): игра в
  `WeaponSystemGun.activatePreLaunchGuidance` (раз за стрельбу) делает `TryToAddTarget` на ВСЕХ директорах орудия —
  одно орудие занимает все (Свердлов: Fore #1 держит 1004 и на Top Bow №1, и на Four Eyes №1 — Fore #2 без канала;
  Adelaide: Mk75 занимает Mk92 и STIR — Mk13 без подсвета). Prefix запоминает у директоров (если их ≥ 2): держал ли цель,
  `_isGuiding`, `_restPositionReached`, `_targetObject`, `_currentShootingMode`; postfix оставляет цель одному — тому,
  что держал её раньше, иначе лучшему из новых (радар, затем больший `_fcSolutionGainRate`), у остальных цель из
  `_associatedTargets` убирается и поля возвращаются. CIWS не трогаем.
  Обзор конфигов (510 кораблей, оружие с ≥ 2 директорами): почти везде директоры — альтернативы со своими каналами
  (Mk13 2×SPG-51, Mk10 2×SPG-55, Mk41 4×SPG-62+SPY-1A, SA-N-7 6–7×Front Dome, Iowa 3×Mk37, Mk45 SPQ-9+SPG-60,
  Adelaide Mk92+STIR), «пост» (нужны все сразу) — разве что Свердлов Top Bow+Four Eyes; связь `TargetTrackSystems`
  (датчик-сопроводитель) в игре есть, но ни один корабль её не задаёт. Поэтому счётчик — сумма по директорам.
  Проверка в HetLeave (`UpdatePins`). Ручной приказ автоматику не вытесняет. «Breaks off» не даём: селектор помечает
  текущую привязанную задачу (`_hetPin`), prefix `WeaponSystem.EndEngageTask` внутри HandleEngageTasks этого юнита
  пропускает вызов для всех систем, кроме её пусковой (других EndEngageTask в HandleEngageTasks кораблей нет).
* Колонка STATE 190 px, AMMO 104 px (большие запасы «12 345/12 345» влезают; за счёт имени); STATE обрезается по
  ширине; заголовок STOCK → AMMO; фон TextBlock вкладки прозрачный.
* Кнопки ■ / ↻: свои цвета (красные / синие тона, подсветка под мышью), значки рисуются в 12×12 по центру.
  Цель своего юнита — «Track N (имя)» по `GetMapContactID()`.

## Версия
* 1.0.0 (в работе): выделен из Smart Windows; шестерёнка и (i) на кнопке вкладки.
* Версия: константа `PluginVersion` (BepInPlugin, ACPlugin, лог) + `<Version>` в csproj — менять обе.
