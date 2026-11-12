# Интеграции

Контракты и результаты текущего совместного этапа: [Integration Wave 1](Reports/IntegrationWave1/README.md).

Точные минимальные версии для EasyInteraction 0.3.0: [таблица совместимости Samples](../README.md#совместимость-samples). Для общего ввода нужен EasyInput 0.2.0+ с его sample EasyInteraction; для общего UI — EasyUI 0.2.0+ с выбранным адаптером.

Адаптеры — Samples с отдельными asmdef. Сначала установите зависимость, затем импортируйте нужный Sample. Это обычные исходники проекта: их можно адаптировать без изменения Core. Повторный импорт Sample может перезаписать вашу копию; сохраняйте изменения в Git.

## UI и Localization

Импортируйте Interaction Gallery с UGUI 2.0. Добавьте Interaction HUD на Actor с Presenter: Canvas, список/круг и при необходимости EventSystem создаются автоматически. Layout меняет представление, SetMenuOpen открывает меню. Выбор фиксируется, курсор восстанавливается при закрытии. Presenter предоставляет причины, иконки и прогресс для собственного UI; Canvas не обязателен для Core.

Большие наборы команд разбиваются на страницы: до пяти строк или шести радиальных кнопок. Previous/Next доступны мышью и через навигацию UI; из кода есть SetPage, PageIndex и PageCount. Переключение страницы меняет выбранную команду; недоступные действия объясняют причину в основной подсказке.

Для небольшой игры достаточно InteractionTextProvider. Для Unity Localization импортируйте Sample, назначьте таблицу и провайдер Presenter. Записи ищутся по Label Key / Text Key; при загрузке таблицы показывается fallback. Адаптер обрабатывает асинхронную загрузку и смену языка.

Для общего ввода используйте **EasyInput / Sample EasyInteraction**. Его EasyInteractionInput реализует IInteractionInputSource из Core. Presenter автоматически предпочитает активный адаптер перед standalone InteractionInput. Явный Input Source имеет приоритет; выключенная явная ссылка остаётся назначенной, но не поставляет подпись/иконку/настройку удержания. Это предотвращает чтение устаревшего legacy reader. PresentationChanged обновляет Prompt на ближайшем LateUpdate, `presenter.Refresh()` — немедленно. Иконка доступна в `InteractionPrompt.BindingIcon`.

Окна, вложенная навигация и блокирующий UI общего проекта принадлежат **EasyUI**. Его отдельный адаптер получает Presenter. UI вызывает Activate/Release, а аналоговое управление — `presenter.SetValue(action, value)`. `actor.GetActive(InteractionInputGesture.Analog)` позволяет reader найти действие, начатое кнопкой UI. Во время окончания аналогового управления EasyInput может ждать нейтрали стика, прежде чем возвращать Look: это предотвращает скачок камеры. Самостоятельная Gallery сохраняет свой лёгкий демонстрационный HUD и legacy input.

## EasyResources

Нужны публичные API `ResourceContainer.TryTransferTo`, `ResourceTransaction` и частичный `ResourceReservation.TryCommit`. Этот выпуск адаптера требует EasyResources 0.3.0+.

1. Добавьте ResourceContainer на Player Root.
2. На объект добавьте Resource Pickup Action или Resource Transfer Action; настройте ресурс, количество и направление. Запас принадлежит контейнеру объекта.
3. Для платного действия добавьте Resource Interaction Cost с ценой и при необходимости Action ID. Пустой Container означает инвентарь Player Root. Installments > 1 оплачивает завершённые доли прогресса, последняя доля ждёт успеха. Отмена возвращает неоплаченный остаток.

Цена сама проверяет средства. Resource Condition нужен для отдельного требования, например ключа. Если тот же ресурс расходуется ценой, задайте условию **Phase = Start**, чтобы Continue не отклонил действие после резервации/оплаты. Передача использует атомарный API EasyResources с ограничениями назначения.

## EasyBuilding

Нужны `IBuildingSessionProvider`, `BuildingActor.GetSession`, `BuildingSystem.CreateSession` и `TryRemove`. Building Action предлагает Remove или SelectForPlacement. На Player Root используется BuildingInteractionContext с BuildingActor. Контекст делегирует `GetSession(system)` канонической сессии этого Actor/System и сам ею не владеет. BuildingPlacer с тем же Actor видит тот же выбор; выключение/уничтожение контекста не Dispose чужую сессию. Превью/интерфейс подтверждает размещение через EasyBuilding. Выбор разных игроков не смешивается; CreateSession остаётся способом явно создать независимую сессию.

Демонтаж передаёт BuildingActor в правила EasyBuilding; отклонение не публикует Committed. Для отклика после разрушения размещайте Action на управляющем объекте, живущем дольше постройки. Строительство, владение и расчёт цены остаются в EasyBuilding.

## EasyCombat и EasyFeedbacks

Combat Interaction Guard ставится рядом с Actor; Health ищется у него/родителя или назначается явно. Урон/downed могут прервать действие; смерть отключает Actor. Revive и Health.StateRestored после CombatSaveSystem синхронизируют жизнь и возвращают Actor только если guard сам отключил его. Actor, выключенный заранее другой системой, остаётся выключенным. Повторное восстановление мёртвого состояния не теряет владение этим отключением. Восстановление живого combat state тоже отменяет прежнюю активную операцию. Health Condition задаёт требования без копирования боевых правил.

Перед общим restore игры сначала вызовите `actor.Cancel()`, затем восстанавливайте ресурсы, мир и состояние Movement/Combat. Так transient input/cost leases закрываются до загрузки позиции и скорости; StateRestored остаётся дополнительной защитой, а не заменой порядка общего сохранения.

Interaction Feedbacks связывает Started, Committed, Completed, Cancelled, Failed с FeedbackPreset. Running создаёт цикл на конкретный ticket; Pause/Resume/Stop следуют действию. Для звука контакта используйте Committed, для окончания работы — Completed. У мгновенного действия они идут подряд, поэтому назначайте один нужный отклик. Музыкой и эффектами управляет EasyFeedbacks.

`InteractionFeedbacks.ContextProvider` принимает MonoBehaviour с `IFeedbackContextProvider`. Пустая ссылка ищет активного провайдера на Player Root и его родителях. Например, PlayerFeedbackContext из EasyInput связывает отклик с устройством конкретного игрока. В Prepare передаётся полный FeedbackContext с источником/позой действия; результат используется без пересоздания и потери параметров. Неверный тип или выключенная явная ссылка поясняется в инспекторе. Без провайдера сохраняется прежнее поведение EasyFeedbacks.

Для player-контекста используйте `context.WithDefaultGamepad(Player ? Player.ActiveGamepad : null)`: он сохраняет явный выбор и задаёт explicit-none при отсутствии геймпада этого игрока. `WithoutGamepad()` явно запрещает вибрацию; `HasExplicitGamepadTarget` отличает это от старого fallback на Gamepad.current. Проверка только TargetGamepad != null теряет смысл explicit-none. Исторические From/default/WithGamepad(null) сохраняют старую семантику; player-провайдер должен использовать WithDefaultGamepad.

## EasyMovement / ECM2 и Navigation

На FPS-игроке EasyMovement добавьте EasyMovement Interaction Motor, назначьте Character при необходимости. View использует Look Pivot, Player Root — Transform Character, в том числе когда Actor находится на камере. Approach Point/Participant Position сначала подводит тело игрока к месту. Motor вызывает публичный API ECM2 и сохраняет столкновения. Это прямой подход; обход препятствий требует другого мотора.

Нужен EasyMovement 5.1.0+ с `Character.AcquireInputBlock(owner)`, `InputBlocked` и `InputBlockedChanged`. Motor и EasyMovement Interaction Lock владеют отдельными IDisposable-запросами. Они не меняют enabled у старого reader; и legacy reader, и EasyInput уважают блок Character и не пишут даже ноль поверх скриптового движения. Lock действует на канале body и, при Lock At Approach Point, во время использования места. Stop/cancel/disable/destroy освобождает только собственный запрос; чужая блокировка остаётся. Старое поле movementInput сохранено для сериализации/API и скрыто в инспекторе. Для движущих тело действий используйте общий канал body.

Для NPC импортируйте Navigation и добавьте NavMesh Interaction Motor на Actor с NavMeshAgent. Нужен построенный NavMesh. Восстанавливаются stopping distance, управление поворотом и при необходимости предыдущий маршрут. Для недостижимых мест назначайте Timeout.

## Netcode for GameObjects

Проверено с NGO **2.7.0** и разрешённой Package Manager версией Transport **2.7.4**.

1. Установите NGO, импортируйте Netcode Sample, настройте NetworkManager и транспорт.
2. На принадлежащий игроку prefab с NetworkObject добавьте Network Interaction Actor. Actor и NetcodeInteractionCommands подключатся автоматически. Существующие Input/Presenter работают через адаптер команд.
3. Целям назначьте одинаковые уникальные Persistent ID на сервере/клиентах и стабильные Action ID. Соответствующие объекты должны существовать на обеих сторонах.
4. Синхронизацию положения/взгляда игрока, Rigidbody и создание/уничтожение объектов настройте средствами NGO проекта. Проверка взаимодействия использует серверную геометрию.

Сервер получает Actor из владельца RPC; клиент не выбирает чужого участника. InteractionAuthority проверяет геометрию, правила, занятость, последовательность и ID запуска. Быстрое отпускание до ответа связывается с номером исходного запроса. Есть ограничение частоты и очистка при отключении.

Передаются состояние действий/прогресс, снимки IInteractionState, начальное состояние при подключении и изменения механики во время работы. Монотонные ревизии отклоняют старые снимки. Внешние изменения публикуйте через `PublishTargetState(target)`. Отсутствующие цели повторно ищутся до 10 секунд; при более позднем спавне опубликуйте состояние заново. Сетевой JSON выделяет память и не входит в измерение ядра без GC.

Камеры, AudioListener и IInteractionLocalView включаются только у владельца. Удалённые анимации/эффекты, физические Transform и серверная валидация движения относятся к сетевому слою игры. Для AnimationEvent нужен работающий серверный Animator или свой `ticket.Commit`; клиентский clip не подтверждает эффект автоматически.

Для другого транспорта используйте Core InteractionAuthority/InteractionCommandAdapter. В Process передавайте Actor аутентифицированного соединения.

## XR Interaction Toolkit

Проверено с **XRI 3.2.2**. На GameObject каждого XR Interactor добавьте XR Interaction Actor и задайте Player Root. Адаптер назначает View/Hand Anchor и Explicit. На предмете используйте **XRSimpleInteractable + XR Interaction Action**, назначьте Core Action; Activate Action задаёт дополнительную команду.

Для двух рук: Grab Action с Max Participants = 2 и два XR Interaction Actor. Rigidbody ведёт Core Grab, поэтому не добавляйте конкурирующий XRGrabInteractable. Отмена Core снимает выбор XRI; повторный выбор ждёт отпускания кнопки. XRI-фильтр проверяет доступность.

Тесты вызывают настоящие события менеджера XRI и проверяют двух участников/отмену. Физический HMD не тестировался; XR Origin, устройства и привязки настраиваются в игре.
