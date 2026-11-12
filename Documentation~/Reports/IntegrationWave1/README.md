# EasyInteraction — Integration Wave 1

Поправка GC: исторические 104/104 и 78/78 включают проверки на некалиброванном счётчике. Их успешное завершение не подтверждает zero GC. Два затронутых теста заменены на калиброванный main-thread GC.Alloc; результаты повторной проверки опубликованы [отдельно](../GcRecorderRepair/README.md). Исходные JSON не изменены.

Проверка 12 ноября 2026, Unity 6000.3.22f1. Версия пакета сохранена (0.2.0); общий manifest, другие пакеты и EasySuite не изменялись. Выпуск остаётся у координатора.

## Публичные контракты

- `EasyInteraction.IInteractionInputSource`: BindingLabel, BindingIcon, ToggleInsteadOfHold, PresentationChanged. Legacy InteractionInput реализует интерфейс. Событие учитывает изменения action, map и asset, включая rebind.
- `InteractionPresenter.InputSource` — явная MonoBehaviour-ссылка; ActiveInputSource и ToggleInsteadOfHold — разрешённые значения. Авторазрешение выбирает активный адаптер на Actor перед legacy reader. Выключенный явный источник остаётся назначенным, но не читается. Изменение источника корректно отписывает старый; Editor-настройка не запускает runtime-подписки.
- `InteractionPrompt.BindingIcon`; сохранён прежний constructor, добавлен overload со Sprite в конце.
- `InteractionPresenter.SetValue(action, value)` — UI analog с маршрутизацией через CommandAdapter. `InteractionActor.GetActive(gesture)` находит в том числе действия, начатые UI.
- `EasyMovementInteractionMotor.IsApproaching`; Motor и Lock используют независимые Character.AcquireInputBlock. Поля movementInput сохранены для старых сцен/API, но не управляют enabled и скрыты в Inspector.
- `BuildingInteractionContext : IBuildingSessionProvider` делегирует BuildingActor.GetSession. Контекст не создаёт независимый словарь и не Dispose заимствованную сессию.
- `CombatInteractionGuard` подписан на Health.StateRestored/Revived; отменяет старую операцию, восстанавливает только своё отключение Actor. Повторный dead restore не теряет владение; ранее выключенный Actor остаётся выключенным.
- `InteractionFeedbacks.ContextProvider` принимает IFeedbackContextProvider; пустая ссылка ищет активный контекст Player Root/родителей. Возвращённый контекст сохраняется целиком. Inspector поясняет неверную/выключенную ссылку.

## Настройка в общем проекте

1. На FPS-игроке EasyMovement используйте один PlayerInputSettings и EasyMovementInput из EasyInput. Character продолжает двигаться скриптовыми командами во время InputBlocked; ручные readers не пишут поверх них ноль.
2. Добавьте Actor/Presenter и EasyInteractionInput из EasyInput. Назначьте Player. Presenter найдёт активный источник; InputSource нужен для явного выбора или источника на другом объекте. UI использует Activate/Release/SetValue.
3. Для подхода добавьте EasyMovementInteractionMotor; для использования места — Lock. Character назначается один раз, Player Root соответствует телу Character, а не высоте камеры. Для движущих тело действий используйте канал body и точку на нужной позиции тела.
4. BuildingPlacer.Actor и BuildingInteractionContext.Actor должны указывать на одного BuildingActor. Тогда выбор через Interaction сразу попадает в Placer.Session. Для явно независимого инструмента остаётся BuildingSystem.CreateSession.
5. CombatInteractionGuard получает Health. Перед общим restore сначала actor.Cancel(), затем восстанавливайте ресурсы/мир/Movement/Combat; StateRestored дополнительно защищает от старой операции.
6. Для контекста отклика назначьте PlayerFeedbackContext из EasyInput в InteractionFeedbacks.ContextProvider либо разместите его на связанном Player Root. Формирование адреса/профиля устройства остаётся у EasyInput/Feedbacks.

## Проверки

Совместный окончательный прогон: **104/104 PlayMode**, 0 failed/skipped/inconclusive. [Полный JSON](playmode-main.json). [Компиляция](compile-main.json): completed, failed=false, compilationFailed=false. После тестов исходная EasySuiteLab.unity восстановлена без dirty, PlayMode/transition/compile/update остановлены: [до](editor-before.json), [после](editor-after.json). 71 тест ядра и 33 теста интеграций/представления/shared input. Проверены camera-hosted Actor, пересекающиеся владельцы, disable/destroy, отмена от restore, новый контекст и отсутствие callback-эффекта после disable.

[Самостоятельная установка](playmode-standalone.json): 78/78 PlayMode (71 core + 7 PresentationTests) без других Easy-пакетов. [Отдельный реальный first-run Gallery](gallery-playmode.json): 1/1 — LoadScene, E/Q, Rigidbody restore, legacy InputSource, меню, дверь и AnimationClip. [Development Windows Player с Gallery](gallery-build.json): Succeeded, 0 ошибок и предупреждений.

[Первый script-eval smoke](cli-smoke-attempt.json) не получил синтетический E в контексте Editor-команды. Он сохранён как неуспешная попытка и не используется как доказательство. Окончательная проверка выполнена через UnityTest в настоящем игровом кадре; runtime-код для обхода не менялся.

В интеграционном тесте обзора проверяются обе фазы: после окончания analog запрос самого адаптера освобождён, но Look остаётся закрытым до нейтрали стика; затем он возвращается. Это защита EasyInput от скачка камеры, а не утечка запроса.

## Ограничения

Проверены API, реальные компоненты и виртуальные Input System устройства. Физические контроллеры/вибрация/звук не проверялись. Общий сквозной стенд и полный аудит набора пакетов принадлежат координатору. Standalone Gallery использует собственный небольшой HUD; модальные/вложенные окна общего проекта обслуживает EasyUI.

Случай keyboard-only игрока закрыт расширением Feedbacks/Input: HasExplicitGamepadTarget и WithoutGamepad отличают explicit-none от исторического fallback. PlayerFeedbackContext использует WithDefaultGamepad. Наш реальный shared-input тест подтвердил сохранение явного target/none и explicit-none после перехода владельца на клавиатуру; InteractionFeedbacks передаёт результат Prepare целиком. Проверка реальных RMBL-команд принадлежит владельцам Feedbacks/Input; физическое устройство здесь не тестировалось.

Отчёты сохраняются в Audits/EasyInteraction/IntegrationWave1 и копируются рядом с этой инструкцией. Подготовительные скрипты — Library/EasyInteractionWork/IntegrationWave1. Чистый проект — Audits/EasyInteraction/IntegrationWave1/CleanProject, вне Temp.

