# Исправление измерения GC — 12 ноября 2026

`GC.GetAllocatedBytesForCurrentThread()` в общем Unity 6000.3.22f1 Editor вернул 0 даже для положительного контроля `new byte[512]`. Поэтому прежние выводы об отсутствии аллокаций у EasyInteraction отозваны. Это относится к двум allocation-тестам и к `allocatedBytes` / `referenceAllocatedBytes` старого benchmark. Исторические флаги `passed=true` не доказывают zero GC.

Исходные JSON в `Documentation~/Validation`, `Documentation~/Validation-v0.2` и `Documentation~/Reports/IntegrationWave1` сохранены без изменения. CPU, сравнение выбранных целей, физические и функциональные результаты не являются измерением GC и рассматриваются отдельно. `holdAllocatedBytes` получен другим счётчиком, `GC Allocated In Frame`: он относится ко всему процессу/кадру, а не только к Grab.

## Исправление

- Два теста и стенд используют `ProfilerCategory.Internal`, `GC.Alloc`, `SumAllSamplesInFrame | CollectOnlyOnCurrentThread`. Считается `ProfilerRecorderSample.Count`, а не `Value`.
- Перед замером счётчик обязан увидеть отдельное выделение массива 512 байт. Benchmark также прогревает обёртку измерителя и проверяет ноль событий в пустой области (`allocationEmptyControlEvents`). Прогрев выполняется с включённым Profiler. Измеряемая синхронная область не пересекает кадры и не содержит NUnit, логирование или сериализацию. Прежнее состояние Profiler восстанавливается при выходе.
- Недоступный/не прошедший калибровку измеритель даёт `Inconclusive` в тесте. Benchmark явно записывает причину и `passed=false`.
- JSON schemaVersion=2 сохраняет байтовые поля: `0` только при подтверждённых нуле событий; `-1` при ненулевых событиях с неизвестным размером, недоступности или не выполнявшемся замере. `allocationEvents` / `referenceAllocationEvents` содержат число событий либо `-1`, а поля `allocationMeasurementAvailable` отдельно указывают доступность. У Raycast нет reference-замера: его поля равны `-1` / false.
- Benchmark пишет новый файл; повторное использование существующего `--report` пути не перезаписывает raw-данные.

Runtime API, зависимости и версии этим исправлением не меняются. Профилировочный код остаётся только в Tests и Tools~ / локальном стенде.

## Проверка

Unity 6000.3.22f1, общий Games Factory Editor, кандидат пакета 0.3.0 от координатора. [Компиляция тестов](compile.json) и [окончательная компиляция benchmark](benchmark-compile-final.json): ошибок нет.

| Проверка | Результат | Raw-отчёт |
| --- | --- | --- |
| `SelectionAndWarmedInstantExecutionDoNotAllocate` | Passed; положительный контроль сработал, 128 прогретых пар выбора/мгновенного действия — 0 событий на главном потоке | [focused-1.json](focused-1.json) |
| `WarmPresenterSourceResolutionDoesNotAllocate` | Passed; положительный контроль сработал, 128 прогретых Refresh — 0 событий на главном потоке | [focused-2.json](focused-2.json) |
| Benchmark recorder + JSON round trip | Passed: положительный контроль 1; пустой контроль 0; пустой замер 0; выделение массива 1; намеренно недоступный замер −1 | [benchmark-recorder-smoke-final.json](benchmark-recorder-smoke-final.json) |

Итого **2/2 PlayMode**, без failed/skipped/inconclusive. Проверка benchmark использует его настоящую обёртку измерителя и настоящие типы JSON через отдельную Editor-команду. В JSON подтверждены три разных результата: доступный ноль событий/0 байт; доступное событие/неизвестный размер −1; недоступный замер/−1. Вложенный `passed=false` — намеренный неполный smoke-report, а не результат полного benchmark.

[Первая попытка smoke](benchmark-recorder-smoke.json) обнаружила аллокацию холодной обёртки и корректно отклонила измеритель. После прогрева обёртки пустой контроль равен нулю. Неуспешная попытка сохранена отдельно.

Полный нагрузочный Player повторно не запускался. Эти результаты подтверждают только указанные короткие Editor-области; они не доказывают отсутствие аллокаций во всех старых нагрузочных сценариях, на других платформах или во всём кадре. CPU с включённым Profiler также нельзя напрямую сравнивать с прежним режимом измерений.

Сохранность **36 исторических raw JSON** подтверждена SHA-256: [проверка](preservation.json), [исходные хеши](historical-raw-before.json). Копии PresentationTests и benchmark в локальном стенде совпадают с пакетом: [source-copies.json](source-copies.json). Runtime API не менялся; текущие package.json/asmdef, версия и floors от координатора сохранены.

Общий Editor освобождён после завершения компиляции и тестов. EasySuiteLab.unity восстановлена, dirty/PlayMode/transition/compile/update=false, Profiler возвращён в исходное false: [до](benchmark-editor-before.json), [после](benchmark-editor-after.json). Общие suites/build выполняются отдельно координатором.

Семантика options и Count: [Unity ProfilerRecorderOptions](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.html), [ProfilerRecorderSample.Count](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderSample.Count.html).
