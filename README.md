# EasyInteraction 0.3.0

Переиспользуемые взаимодействия для Unity 6.3: выбор объекта, несколько действий, физическое удержание, механика, совместная работа и адаптеры других систем. Основной сценарий — Actor на игроке и Action на объекте. Дополнительные правила включаются по необходимости.

## Первый запуск

1. В Unity 6.3 откройте **Window > Package Manager > + > Install package from disk** и выберите `package.json` этого пакета. Установится Input System 1.20.0. В **Project Settings > Player > Active Input Handling** используйте **Input System Package (New)**; перезапустите Editor, если он попросит.
2. Выберите корень игрока и выполните **Tools > Easy Interaction > Add Player Components to Selection**. В `Interaction Actor > View` назначьте его Camera. Если Actor размещён на камере, укажите корень игрока в `Player Root`.
3. Создайте Cube в двух метрах перед камерой и добавьте **Grab Action**. Target и Rigidbody добавятся автоматически. В Play Mode **E** берёт предмет, **Q** отпускает. Rigidbody остаётся динамическим и сталкивается с миром.

Для FPS-игрока в Games Factory используйте EasyMovement на ECM2. Пакет взаимодействий не создаёт второй контроллер передвижения.

**Готовый пример:** в Package Manager импортируйте Sample **Interaction Gallery** (нужен Unity UI / UGUI 2.0). Откройте его `InteractionGallery.unity` и нажмите Play. Для пересоздания примера выполните **Tools > Easy Interaction > Create Interaction Gallery**. Пример включает камеру стенда, двери, предмет, сокет, несколько действий, анимацию, последовательность, рычаг, ящик и совместную работу. Prefabs и UI можно использовать отдельно.

## Встроенные возможности

| Задача | Что использовать |
| --- | --- |
| Точный прицел / удобное наведение | Actor: Raycast / SphereCast / Focus |
| Курсор, ближайший предмет, касание, выбор кодом | Cursor / Nearest / Contact / Explicit |
| Крупный объект или отдельные кнопки панели | Focus Point, Extra Focus Points, дочерние Interaction Point с Action IDs |
| Несколько команд на одном предмете | Несколько Action с уникальными ID; Tab / F; список или круговое меню |
| Нажатие, подтверждение, удержание, переключатель, аналоговый ввод | Action > Input Gesture |
| Задержка, пауза, событие анимации | Timed / AnimationEvent, Pause/Resume, Interaction Animation Bridge |
| Этапы со временем и внешним сигналом | Sequence Action |
| Дверь, переключатель | Door Action / Event Action |
| Перенос, бросок, вращение, осмотр, две руки, передача | Grab Action + Inspect Action |
| Установка и извлечение совместимого предмета | Socket Action + Socketable |
| Рычаг, вентиль, ползунок, физический joint | Mechanical Action |
| Общая работа и сохранение её прогресса | Cooperative Action |
| Эксклюзивный объект, группа, позиции, очередь | Use Scope, Max Participants, Participant Position, Enqueue |
| Требования | Flag / Distance / Facing / Capability Condition, Condition Group; All / Any; Start / Continue / Both |
| Подсказка, причины отказа, подсветка | Interaction Presenter, Text Provider, Highlight |
| Сохранение состояния мира | CaptureState, TryRestoreState, IInteractionState |
| Одинаковые настройки объектов | Interaction Preset; состояние остаётся у компонентов |

Заготовки находятся в **GameObject > Easy Interaction**: Camera, Grab Item, Door, Socket, Physics Lever. **Tools > Easy Interaction > Validate Open Scenes** проверяет коллайдеры и ID. Дополнительные параметры инспекторов сгруппированы в раскрывающиеся разделы; диагностика объясняет причину недоступности.

## Управление

**E / gamepad A** — действие, **Q / B** — отмена, **Tab / RB** — следующая команда, **F / X** — вторая команда, **V / LB** — следующая цель. **G / Y** — бросок, **R + мышь** — вращение, колесо — расстояние удержания. Аналоговая механика принимает горизонтальное движение мыши или правый стик. **C / Select** открывает меню из UI sample.

Input Actions можно назначить явно или предоставить одноимённые действия через `PlayerInput`. `Toggle Instead Of Hold` запускает подтверждение одним нажатием и отменяет следующим; время подтверждения сохраняется. Подпись клавиши берётся из Input System, включая переназначение.

При использовании EasyInput импортируйте его Sample **EasyInteraction**. Presenter автоматически получает подпись, иконку и hold-toggle от активного `IInteractionInputSource` на Actor; для явного выбора назначьте `Input Source`. Выключенный источник не читается. Для собственного UI используйте `Presenter.Activate`, `Release` и `SetValue` — наличие legacy InteractionInput не требуется.

В Gallery: RMB + мышь — осмотр, F1–F4 — станции, 1–6 — режимы поиска, Z — список/круг, L — замок, M — масса, B — стена, N — второй участник, H — сброс общей работы. Это лабораторная камера.

## Короткое API

```csharp
actor.RefreshTarget();                       // автоматически вызывается в Update
var available = actor.Check(action);          // IsAvailable, Code, Reason, Diagnostic
actor.TryExecute(action);                    // проверяет текущий выбор заново
actor.TryBegin(action, out var ticket);       // прямой запуск с проверкой геометрии и правил
actor.Pause(action);
actor.Resume(action);
ticket.Cancel();                             // отменяет только этот запуск
actor.ActionStateChanged += change => { /* Started, Committed, Completed, Cancelled... */ };
```

`true` при запуске означает принятие операции; окончательный результат приходит событием. `Committed` обозначает применение игрового эффекта; `Completed` — успешное завершение. Для захвата это разные моменты. Reason предназначен игроку, Diagnostic — разработчику.

Своё действие обычно требует одного метода:

```csharp
public sealed class SwitchLightAction : EasyInteraction.InteractionAction
{
    [UnityEngine.SerializeField] private UnityEngine.Light lamp;
    public override EasyInteraction.InteractionAvailability GetAvailability(
        EasyInteraction.InteractionActor actor) => lamp
        ? EasyInteraction.InteractionAvailability.Available
        : EasyInteraction.InteractionAvailability.Unavailable(
            EasyInteraction.InteractionFailure.InvalidConfiguration,
            "Light is unavailable.", diagnostic: "Assign Lamp.");
    protected override bool OnExecute(EasyInteraction.InteractionActor actor)
    {
        lamp.enabled = !lamp.enabled;
        return false; // true оставляет длительное действие активным
    }
}
```

Жизненный цикл, владение, сохранение и расширение: [API.md](Documentation~/API.md).

## Интеграции и проверка

Samples: **EasyResources**, **EasyBuilding**, **EasyCombat**, **EasyFeedbacks**, **EasyMovement**, **Navigation**, **Localization**, **Netcode**, **XR**. Импортируйте нужный Sample после его зависимости. Core не зависит от других Easy-пакетов, NGO, XR, UGUI или Localization. Шаги настройки: [Integrations.md](Documentation~/Integrations.md).

Тесты, сборки, измерения CPU/GC и границы проверки: [Validation.md](Documentation~/Validation.md). Повторяемые стенды: [Tools~/README.md](Tools~/README.md).

Система расширяема под игровые правила. Репликация передвижения, общий формат сохранения игры, сложный маршрут подхода и нестандартная физика остаются задачами конкретного проекта.

## Совместимость Samples

Samples этого выпуска требуют EasyInteraction **0.3.0+**. Core устанавливается без других Easy-пакетов. Установите зависимости только выбранного sample, затем импортируйте его один раз через Package Manager. В таблице указаны поддерживаемые минимумы этой версии адаптеров: у старых выпусков может не быть нужного API. Зависимости Unity из package.json устанавливаются автоматически.

| Sample | Дополнительные требования | Импортировать заранее |
|---|---|---|
| Door and Crate | Нет | — |
| Interaction Gallery | uGUI 2.0.0+ | — |
| EasyResources | EasyResources 0.3.0+ | — |
| EasyBuilding | EasyBuilding 0.3.0+ | — |
| EasyCombat | EasyCombat 0.7.0+ | — |
| EasyFeedbacks | EasyFeedbacks 1.8.0+ | — |
| EasyMovement | EasyMovement 5.1.0+, ECM2 (separate installation; tested with 1.4.3) | — |
| Navigation | AI module 1.0.0, a baked NavMesh | — |
| Localization | Unity Localization 1.5.13+ | — |
| Netcode | Netcode for GameObjects 2.7.0+, a configured transport | — |
| XR | XR Interaction Toolkit 3.2.0+ (3.x), a configured XR rig | — |

Оставляйте одну копию каждой sample-сборки. Существующие импорты могут оставаться в прежней папке Assets: перед повторным импортом сверяйте изменения, сохраняя свои правки и GUID assets/actions.
