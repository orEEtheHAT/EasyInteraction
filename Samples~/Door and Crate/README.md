# Door and Crate

**Compatibility:** EasyInteraction 0.3.0+. No prerequisite sample imports.

Откройте `DoorAndCrate.unity`, нажмите Play и щёлкните Game.

| Кнопка | Действие |
| --- | --- |
| Правая кнопка мыши + движение | Повернуть неподвижную камеру |
| E / gamepad south | Первое действие цели; повторно — отпустить |
| Q / gamepad east | Отменить захват |
| 1 / 2 | Raycast / Focus — способ выбора цели |
| L | Запереть/отпереть дверь, показать причину отказа |
| M | Масса свободного ящика 2 / 100 кг |
| B | Показать/скрыть перекрывающую стену |
| P | Отключить/включить игрока, проверить отпускание |

`Interaction Camera` содержит **Interaction Actor** и **Interaction Input**. Поле Actor > View указывает на эту камеру.
`Door - hinge and world state` содержит **Door Action** и **Interaction Target**; `Door panel` — дочерний mesh/Collider.
`Crate - dynamic Rigidbody` содержит **Rigidbody**, **Grab Action** и **Interaction Target**.
`Sample controls and hints` содержит **Interaction Sample View**, ссылки на Actor/дверь/ящик/стену и рисует состояние через публичный API.

Меню **Tools > Easy Interaction > Create Door and Crate Scene** повторяет создание сцены; читаемый исходник — `Editor/InteractionSampleBuilder.cs`.
Другие Easy-пакеты не требуются. Для передвижения в своём FPS-проекте используйте EasyMovement на ECM2 и назначьте его камеру в Actor > View.
