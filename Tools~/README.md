# Повторение проверок

Эта папка исключена из импорта Unity. Runtime пакета не зависит от инструментов проверки.

## Тесты

В тестовом проекте установите `com.unity.test-framework` 1.6.0 и временно добавьте `com.oreethehat.easy-interaction` в `Packages/manifest.json > testables`. Для Games Factory соблюдайте блокировку `Temp/UnityEditor.agent-lock` из AGENTS.md на весь цикл правок, компиляции и тестов. После прогона удалите только свою временную запись, сохранив остальные.

```powershell
unity command --project-path "C:\Users\LeshaPC\Games Factory" script recompile
unity command --project-path "C:\Users\LeshaPC\Games Factory" script recompile_status
# Дождитесь completed без ошибок, затем:
unity command --project-path "C:\Users\LeshaPC\Games Factory" test run --mode playmode --filter EasyInteraction.PlayModeTests --filter-type assembly --async-tests
unity command --project-path "C:\Users\LeshaPC\Games Factory" test status --result-only
```

Повторяйте `test status` до `completed`; сохраните JSON. Также можно использовать **Window > General > Test Runner > PlayMode**.

## Нагрузочная сборка

1. Скопируйте `Tools~/Performance` в `Assets/PackageTests/EasyInteraction/Performance` тестового проекта.
2. Создайте пустую сцену, добавьте Camera и пустой GameObject с **Interaction Benchmark**. Сохраните как `Assets/PackageTests/EasyInteraction/Performance.unity`. В Games Factory эта сцена уже создана через CLI.
3. Соберите только эту сцену для Windows x64, **Development Build**, backend **Mono**, без Deep Profiling и Script Debugging:

```powershell
unity command --project-path "C:\Users\LeshaPC\Games Factory" build run --target StandaloneWindows64 --output-path "Builds/EasyInteraction/InteractionBenchmark.exe" --scenes '["Assets/PackageTests/EasyInteraction/Performance.unity"]' --options '["Development","DetailedBuildReport"]' --confirm
unity command --project-path "C:\Users\LeshaPC\Games Factory" build status --result-only
```

4. После `completed / Succeeded` запустите Player с `-batchmode -screen-width 640 -screen-height 360 -screen-fullscreen 0 --report "<absolute report path>.json" -logFile "<absolute log path>.log"`. При автоматическом запуске Windows используйте `Start-Process -WindowStyle Hidden`. Не запускайте компиляцию или другие тяжёлые задачи во время замеров.
5. Player сам завершится после 16 сценариев выбора, 3 сценариев API и 9 сценариев удержания. Без `--report` JSON записывается в `Application.persistentDataPath/EasyInteraction-benchmark.json`.

`meanMs/p95Ms` измеряют синхронный `RefreshTarget` через Stopwatch, `allocatedBytes` — разницу `GC.GetAllocatedBytesForCurrentThread`. Поиск прогревается 64 раза, затем берутся 160 измерений. Полный эталон проверяет видимость всех кандидатов, а затем выбирает лучший с тем же порогом переключения и разрешением ничьих. `mismatches` должен быть 0. Это стенд корректности и сравнения, отдельный режим API в пакет не добавляется.

Для физики `holdMeanUs` — маркер `EasyInteraction.HoldPhysics`, усреднённый по кадрам рендера (в кадре может быть 0/1/несколько FixedUpdate). `holdAllocatedBytes` — GC всего кадра в интервале 1.5 с, включая первое использование, а не только кода захвата. `maximumError` учитывает положение после первых 0.5 с. Затем 1.5 с точка удержания движется по синусоидальной траектории: смещение до 0.45 м, поворот до 20 градусов. После установления проверяется упор за стеной в течение 0.8 с и восстановление Rigidbody при отключении Actor. Успех движения: сохранено владение и ошибка менее 0.5 м.

В версии 0.2 `Raycast switching` наводится на реальные левую/правую цели: targetChanges должен показывать переключения. Эталон Focus проверяет базовый режим с одной точкой и одинаковыми приоритетами; части и дополнительные точки покрыты функциональными тестами.

Сценарии API: мгновенный запуск, Tick таймера и обработка авторитетного запроса. По 128 вызовов прогрева и 512 замеров; проверяются failures = 0 и allocatedBytes = 0. Это измерение Core InteractionAuthority, а не сериализации NGO.

## Адаптеры

Для тестов интеграций скопируйте IntegrationTests в Assets проверочного проекта с соответствующими Easy-пакетами и импортированными Samples. Для XR скопируйте XRTests в проект с XRI 3.2.2 и XR Sample. Запускайте фильтр `EasyInteraction`, чтобы включить все эти сборки.

Для настоящего сетевого прогона установите NGO 2.7.0, импортируйте Netcode Sample и скопируйте NetworkSmoke в Assets. Через `script eval` вызовите `EasyInteraction.Verification.Network.Editor.NetworkSmokeBuilder.Create("Assets/NetworkScene")`. Соберите NetworkSmoke.unity. Запустите два процесса одного Player: `-batchmode -nographics --server --report "<server.json>"` и `-batchmode -nographics --client --report "<client.json>"`, каждый со своим `-logFile`. Используется loopback:17843; Player завершится сам, таймаут 60 с.

Оба отчёта должны иметь passed=true. Проверяются первоначальный снимок, дверь, быстрая отмена до ответа, отказ вне радиуса, непрерывная механика, эксклюзивный захват и отключение. Поле disconnectedCleanup проверяется сервером; false в клиентском отчёте означает отсутствие такой серверной проверки на клиенте.
