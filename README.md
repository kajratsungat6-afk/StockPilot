# StockPilot — Final project: stock, orders and deliveries in functional C#

Console application for a small warehouse: it plans restocking, processes customer orders (with a handling fee calculated by a **native C library**), receives supplier deliveries, produces two reports and saves the updated stock.
Course: Introduction to Functional Programming, 2026–2027. Built step by step in Assignments 5–8 and extended for the final exam (third workflow, updated-stock persistence, command line, source split into files).

## 1. Project information

| | |
|---|---|
| Title | StockPilot |
| Author (individual project) | Kairat Sungat, group IT-2511 |
| Problem | A warehouse must decide what to buy, accept or reject orders and deliveries without ever corrupting its stock. |
| Intended users | purchasing clerk (restock plan, purchasing list), warehouse manager (value report, orders, deliveries) |
| Repository / branch / final commit | https://github.com/kajratsungat6-afk/StockPilot / `main` / tag `final-submission` |

### Workflows (3 — minimum for an individual project)
| # | Workflow | Input | Business rules | Result | Command |
|---|---|---|---|---|---|
| 1 | **Restock planning** | validated products | select low stock (`Quantity <= ReorderLevel`), score with a policy, build an ordered plan with suggested quantities | `RestockPlan` per policy | `restock` |
| 2 | **Order processing** | order requests + stock | sku found, quantity > 0, enough stock, total within the limit, native handling fee, confirm (Pending → Confirmed) | `OrderReceipt` or an explicit error, new stock | `orders` |
| 3 | **Delivery receiving** | delivery lines + stock | reference present and not received before, sku found, quantity > 0, stock after delivery <= 10 000 | `DeliveryReceipt` or an explicit error, new stock and ledger | `deliveries` |

Two related domain models: **Product / Catalog** (stock) and **orders & deliveries** (`ValidatedOrder`, `Reservation`, `ValidatedDelivery`, `DeliveryLedger`). The three workflows form one loop: the purchasing list says what is missing → orders take stock out → deliveries bring it back (the demo shows the total deficit falling from 36 to 0).

## 2. Prerequisites and exact commands
- **Platform declared:** Windows 10/11, **x64**, .NET SDK 8.0 (tested runtime 8.0.31). A 64-bit .NET process needs a **64-bit** native library. The built DLL is included in `native/runtime/win-x64/`; Linux/macOS: `sh native/build.sh`.
- **C compiler (only to rebuild the DLL):** MinGW-w64 `gcc` (64-bit; `gcc -dumpmachine` must start with `x86_64`) **or** MSVC `cl` (Developer PowerShell). Example: `winget install BrechtSanders.WinLibs.POSIX.UCRT`, then open a **new** terminal.
- **Compiler and version used:** `gcc.exe (MinGW-W64 x86_64-ucrt-posix-seh, built by Brecht Sanders, r3) 16.2.0` (`x86_64-w64-mingw32`) on Windows 11, x64.
- The tests use xUnit (NuGet packages are restored by `dotnet test`; internet access is needed the first time).

Working directory for all commands: **the repository root** (the folder with `src`, `tests`, `native`, `data`).
```
build-native.cmd                                        # 1. (re)build the native DLL — optional, a built DLL is included
dotnet test tests/StockPilot.Tests                      # 2. tests (the DLL must exist)
dotnet run --project src/StockPilot                     # 3. whole demo (same as: run.cmd)
dotnet run --project src/StockPilot -- deliveries       # a single command (same as: run.cmd deliveries)
```
Commands: `demo` (default), `restock`, `reports`, `orders`, `deliveries`, `help`. Options: `--products <file>`, `--orders <file>`, `--deliveries <file>`, `--out <folder>` (defaults: `data/products.json`, `data/orders.json`, `data/deliveries.json`, `output`). A command opens only the files it needs.
**Clean extraction check:** unzip into an empty folder → `build-native.cmd` (or keep the included DLL) → `dotnet test tests/StockPilot.Tests` → `dotnet run --project src/StockPilot`.

## 3. Architecture: pure core and side effects
```
data/ products.json, orders.json, deliveries.json, malformed_orders.json     sample data
native/ src/fees.c, include/fees.h, build.ps1, build.sh, tests/fees_check.c, runtime/win-x64/   native library
src/StockPilot/
  Common/      Errors.cs  Maybe.cs  Result.cs                    explicit outcomes (Error, Maybe, Result, Map, Bind, Ensure, Match)
  Products/    Product.cs  ProductInput.cs  ProductFormats.cs     product models, validation of raw rows, JSON text of the stock
  Inventory/   Catalog.cs                                        immutable stock snapshot (Maybe lookups)
  Pipeline/    Stages.cs  Policies.cs  Scoring.cs  RestockPipeline.cs      workflow 1
  Orders/      OrderRequest.cs  OrderStates.cs  ValidatedOrder.cs  OrderWorkflow.cs  OrderInput.cs      workflow 2
  Deliveries/  DeliveryRequest.cs  ValidatedDelivery.cs  DeliveryWorkflow.cs  DeliveryInput.cs        workflow 3
  Reports/     QueryExtensions.cs  Reports.cs  ReportFormats.cs  two LINQ reports and their CSV text
  Native/      NativeMethods.cs  NativeFeeCalculator.cs          P/Invoke binding and the adapter
  IO/          DataFile.cs  ReportFile.cs  AppFlow.cs            file boundary (the only code that touches files)
  App/         CommandLine.cs  Views.cs  Application.cs          command line, console text, commands (the only code that writes output)
  Program.cs                                                      connects the real console, clock and native library
tests/StockPilot.Tests/  Pipeline/ Orders/ ErrorHandling/ Reports/ Deliveries/ App/ Support/
```
| Pure (no Console, no files, no clock) | Side effects (boundary) |
|---|---|
| `Stages`, `RestockPipeline`, `Scoring`, `Policies`, `ProductInput`, `ProductFormats`, `Catalog`, `ValidatedOrder`, `OrderWorkflow`, `ValidatedDelivery`, `DeliveryWorkflow`, `Reports`, `ReportFormats`, `CommandLine.Parse` | `DataFile.Load` (read), `ReportFile.Save` (write), `NativeMethods` (native call, behind the `IFeeCalculator` adapter), `Views` and `Application` (output), `Program` (console, clock) |

The clock is read once in `Program` and passed in; the output writers, the fee calculator and the clock are parameters of `Application.Run`, which is why the whole application is tested without a real console.

## 4. Domain rules and expected results
Sample data: `data/products.json` (8 rows: 6 valid, 2 invalid), `data/orders.json` (8 orders), `data/deliveries.json` (8 deliveries). Expected values were written before the run.

### Workflow 1 — restock pipeline (Stages: select → score → plan)
| Stage | Input → output | Rule |
|---|---|---|
| `Stages.SelectRestockCandidates` | products → candidates | `Quantity <= ReorderLevel` (threshold included, via the extension `LowStock`) |
| `Stages.ScoreCandidates` | candidates + `Func<Product,long>` → scored | the **policy is a delegate argument**, invoked once per candidate |
| `Stages.BuildPlan` | scored → `RestockPlan` | order by score descending, then sku (ordinal); suggested quantity = `2 * ReorderLevel - Quantity` |

`RestockPipeline.Run(products, policy)` composes them. Four policies run on the same data: `Conservative`, `ValueWeighted` (plain delegates) and `Urgent`, `Economy` (partial applications of the curried `Scoring.CreateScore`). Documented difference: **Conservative** orders B-200, A-100, A-200, B-100; **ValueWeighted** puts A-200 (Drill, expensive) first.

### Extension methods (both used by the application)
| Extension | Receiver -> return | Behaviour | Why it belongs on the receiver | Used in |
|---|---|---|---|---|
| `LowStock(this IEnumerable<Product>)` | `IEnumerable<Product>` -> `IEnumerable<Product>` | keeps products with `Quantity <= ReorderLevel` (threshold included); lazy; source not changed | "low stock" is a question about a collection of products, so it reads as a query on the sequence and composes with other LINQ operators | `Stages.SelectRestockCandidates` (pipeline stage 1), `Reports.PurchasingList` |
| `Ensure<T>(this Result<T>, Func<T,bool>, Error)` | `Result<T>` -> `Result<T>` | success stays success if the rule holds, otherwise returns the given error; a failure passes through and the rule is **not** evaluated | it is a step of a `Result` chain, like `Map` / `Bind`, so it must be callable fluently on the `Result` | `OrderWorkflow.Process` (order limit rule) |

### Two LINQ reports (pure; display and export are separate)
| | Report 1: Purchasing list | Report 2: Stock value by category |
|---|---|---|
| Method | `Reports.PurchasingList` | `Reports.ValueByCategory` |
| User | purchasing clerk | warehouse manager |
| Input | validated products from `products.json` | same |
| Filters | `LowStock()` | `Quantity > 0` |
| Stages | Where (LowStock) -> Select -> OrderByDescending + ThenBy -> Sum | Where -> GroupBy -> Select (Count, Sum, Sum) -> OrderByDescending + ThenBy -> Sum |
| Output fields | sku, name, quantity, reorder level, deficit; total deficit | category, products, units, value (minor units); grand total |
| Order | deficit descending, then Sku (ordinal) | value descending, then category (ordinal) |
| **Expected** | B-200 (20), A-100 (8), A-200 (1), B-100 (0); total deficit **29** | TOOLS 2 / 7 / 128000, SUPPLIES 2 / 80 / 30500, HARDWARE 1 / 10 / 3000; grand total **161500** |

Both reports materialise their result (`ToImmutableArray`), so they keep no reference to the source. The reports are exported as CSV (`purchasing_list.csv`, `value_by_category.csv`) by `ReportFile.Save`; formatting is the pure `ReportFormats`.
Invalid rows (negative price, missing sku) are rejected at intake and never reach a report (tested).

### Currying and partial application
`Scoring.CreateScore : Func<ScoreConfig, Func<Product, long>>` — configuration first, product second.
`score = DeficitWeight * (ReorderLevel - Quantity) + (UnitPriceMinor >= ExpensivePriceThreshold ? ExpensiveBonus : 0)`

Partial application (two configured functions from the same curried function):
- `Scoring.Urgent = CreateScore(new ScoreConfig(3, 10_000, 50))`
- `Scoring.Economy = CreateScore(new ScoreConfig(1, 50_000, 0))`

| Product (qty/reorder, price) | Urgent | Economy |
|---|---|---|
| Hammer (2/10, 1500) | 24 | 8 |
| Drill (5/6, 25000) | **53** | **1** |
| Screws (0/20, 200) | 60 | 20 |

Both are passed into `RestockPipeline.Run` as the policy argument: order Urgent = B-200, A-200, A-100, B-100; Economy = B-200, A-100, A-200, B-100 (Drill moves). The configuration is an immutable record captured once; no mutable global is switched.

### Workflow 2 — orders (validate → limit → native fee → confirm → receipt)
`ValidatedOrder.Create` is the validated factory (private constructor): sku found (`Maybe` lookup), quantity > 0, quantity ≤ stock. Distinct cases: `OrderValidation` = `Valid | Invalid`; `Reservation` = `Pending | Confirmed | Rejected`; `TransitionOutcome` = `Applied | NotAllowed`. `OrderWorkflow.Process` is the Result chain: `Create → ToResult → Ensure (limit 100 000 000) → Bind (native fee) → Bind (confirm) → Map (receipt)`. Repeating a confirmation is rejected (`InvalidState`).

| # | Order | Expected |
|---|---|---|
| 1 | 2 x A-100 | `CONFIRMED 2 x A-100, total=3000, fee=215` |
| 2 | 5 x A-200 | `CONFIRMED 5 x A-200, total=125000, fee=3325` |
| 3 | 1 x B-200 | `[InsufficientStock]` (stock 0 is real data, not absence) |
| 4 | 1 x Z-999 | `[ItemNotFound]` |
| 5 | 0 x C-100 | `[InvalidQuantity]` |
| 6 | 40 x C-100 | `CONFIRMED 40 x C-100, total=16000, fee=1300` |
| 7 | 20 x C-100 | `[InsufficientStock]` (10 left) |
| 8 | 1 x (no sku) | `[SkuRequired]` |

Stock after orders: A-100 0, A-200 0, B-100 10, B-200 0, C-100 10, C-200 30.

### Workflow 3 — deliveries (validate → apply to stock → receipt)
`ValidatedDelivery.Create` (private constructor) checks, and reports **every** broken rule with its own code: reference present (`ReferenceRequired`) and not already in the `DeliveryLedger` (`DuplicateDelivery`), sku found (`ItemNotFound`), quantity > 0 (`InvalidQuantity`), stock after delivery ≤ `MaxStockPerSku` = 10 000, **inclusive** (`CapacityExceeded`). `DeliveryWorkflow.Receive` is the Result chain `Create → ToResult → Bind (look the product up, apply) → Map (receipt)`; success returns a **new** `Catalog` and a **new** `DeliveryLedger`, failure returns nothing, so the caller's stock and ledger are never half-updated. `ReceiveAll` applies deliveries in order; each one sees the stock left by the previous accepted one.

Demo flow (after the orders above):
| # | Delivery | Expected |
|---|---|---|
| 1 | D-001: 40 x B-200 | `RECEIVED D-001: 40 x B-200, stock 0 -> 40, value=8000` |
| 2 | D-002: 18 x A-100 | `RECEIVED D-002: 18 x A-100, stock 0 -> 18, value=27000` |
| 3 | D-001: 5 x A-200 | `[DuplicateDelivery]` (repeated operation is rejected, nothing applied twice) |
| 4 | D-003: 5 x Z-999 | `[ItemNotFound]` |
| 5 | D-004: 0 x C-100 | `[InvalidQuantity]` |
| 6 | D-005: 9971 x C-200 | `[CapacityExceeded]` (30 + 9971 = 10 001 > 10 000; 9970 would be accepted) |
| 7 | (no reference): 3 x A-200 | `[ReferenceRequired]` |
| 8 | D-006: 7 x A-200 | `RECEIVED D-006: 7 x A-200, stock 0 -> 7, value=175000` |

Stock after deliveries: A-100 18, A-200 7, B-100 10, B-200 40, C-100 10, C-200 30. Purchasing-list total deficit: **36 after the orders, 0 after the deliveries**. (Run alone, `deliveries` starts from the original stock, so the receipts show 2 -> 20 for A-100.)

### Persistence
Read: `DataFile.Load` (JSON). Write: `ReportFile.Save` — two CSV reports (`purchasing_list.csv`, `value_by_category.csv`) and the **updated stock** `products_after.json` (same schema as `products.json`, so `--products output/products_after.json` continues from where a run stopped). Writes are atomic: temp file, then move.

### Error model (explicit `Result`, no exceptions for business failures)
| Code | Meaning |
|---|---|
| `SkuRequired`, `ItemNotFound`, `InvalidQuantity`, `InsufficientStock` | order/delivery input rules |
| `ReferenceRequired`, `DuplicateDelivery`, `CapacityExceeded` | delivery rules |
| `OrderTooLarge`, `InvalidState` | order limit, illegal state transition (repeat / rejected) |
| `InvalidFeeInput`, `NativeUnavailable`, `NativeRejected` | native adapter: bad input (C not called), library/function not loadable, C returned -1 |
| `FileMissing`, `MalformedData`, `IoError` | boundary problems; every message names the file |
| `InvalidArguments` | wrong command line |

### Deferred execution (`yield return`) and materialisation
`OrderWorkflow.ProcessLazily(requests, catalog, fees, at)` returns `IEnumerable<OrderStep>` built with `yield return`: one step per order, each carrying the outcome and the catalog after that order (project values).
- Creating the enumerable only checks the arguments; **no order is processed and no fee calculated** until it is enumerated (test probe counts pulls and fee calls — the counters live in the test, not in production code).
- `First()` processes exactly one order.
- **Materialisation point:** `OrderWorkflow.ProcessAll` enumerates once into an `ImmutableArray` of outcomes.
- **Repeated enumeration:** *before* materialisation every enumeration re-reads the source sequence and re-processes the orders (tested: 2 enumerations -> 6 pulls). *After* materialisation the snapshot is reused freely (tested: 1 enumeration, 3 pulls, identical results on every use). No file is read inside the lazy sequence, no resource is held by it, and business state is never changed: `Catalog` is immutable.

## 5. Resource ownership and retention note

### Resource ownership
| Resource | Owner | Release point | Exception path |
|---|---|---|---|
| `FileStream` + `StreamWriter` in `ReportFile.Save` | `ReportFile.Save` | end of the `using` block (flush + close) | released also when `Write` throws; temp file deleted; old file untouched; `IOException` / `UnauthorizedAccessException` become `Failure(IoError)` |
| `File.ReadAllText` in `DataFile.Load` (convenience API) | the API itself | opens and closes the file internally, before returning | released on success and on failure (tested: files can be deleted afterwards) |
| lazy `ProcessLazily` | holds no disposable resource | — | — |
| `products_after.json` (updated stock) | `ReportFile.Save`, same method as the CSV reports | end of the `using` block | same as above: temp file, then move over the target |

Data is loaded inside a bounded scope (`ReadAllText`) and returned as a snapshot for later pure processing; garbage collection is not relied on to release file handles.

### Retention and allocation
- **Where datasets are materialised:** `ProductInput.ValidateAll` -> `ImmutableArray<Product>`; `Catalog` (immutable dictionary); both reports (`ToImmutableArray`); `OrderWorkflow.ProcessAll` (outcomes array); `DataFile.Load` (the text, then parsed lists); `DeliveryWorkflow.ReceiveAll` (outcomes array).
- **Deferred queries / closures:** `ProcessLazily` keeps references to the request sequence, the starting catalog and the fee calculator until enumeration ends. `ProcessAll` enumerates it immediately and drops it. `LowStock` is lazy but is consumed immediately by the caller. The static curried scores capture only an immutable `ScoreConfig`.
- **Design decision (avoids unnecessary retention):** `ProcessAll` keeps only the **last** catalog; the intermediate catalogs of earlier steps are not stored in the result, so they become collectable. The delivery workflow follows the same rule: a rejected delivery returns no new catalog, an accepted one returns a new catalog and a new ledger that share structure with the old ones (`ImmutableDictionary.SetItem`, `ImmutableHashSet.Add`). Also `Catalog.WithQuantity` uses `ImmutableDictionary.SetItem`, which shares structure instead of copying the whole dictionary.
- **Long-lived collections, caches, event subscriptions:** none are used. `DeliveryLedger` lives for one run only and is passed explicitly (it is not a static or cached collection); (no static mutable collections, no cache, no events). Static fields are stateless delegates and options.
- **Streaming vs snapshot:** streaming (`ProcessLazily`) processes one order at a time and can stop early without reading the rest, but the source must stay available and re-enumerating repeats the work. A snapshot (`ProcessAll`) uses memory for all results but is stable, can be reused, and holds no source or file open. This project uses a snapshot for files and reports.
- No memory leak is claimed or fixed.

## 6. Native C library
**Files:** `native/src/fees.c`, `native/include/fees.h`, `native/build.ps1` (Windows), `native/build.sh` (Linux/macOS), `native/tests/fees_check.c` (manual check without .NET).
**Export:** `int32_t calc_handling_fee(int32_t order_total_minor, int32_t quantity)`

| Item | Contract |
|---|---|
| Purpose | handling fee of an order, added to every confirmed order receipt |
| Input types | `int32_t order_total_minor` (minor currency units, 1500 = 15.00), `int32_t quantity` — fixed-width types, no platform-dependent sizes |
| Valid range | `0 <= order_total_minor <= 1 000 000 000`, `1 <= quantity <= 10 000` |
| Output | `int32_t` fee in minor units: `100 + 20 * quantity + round_half_up(order_total_minor * 2.5 %)` |
| Rounding | integer arithmetic, 2.5 % = `25/1000`, half up: `(total * 25 + 500) / 1000` (64-bit intermediate) |
| Invalid input | returns `-1` (a valid fee is never negative) |
| Memory | none allocated, no handle returned: nothing to release; function is pure and deterministic |
| **Manually checked example** | total 10000, quantity 3 -> `100 + 60 + 250 = 410`; total 10020, quantity 1 -> 2.5 % = 250.5 -> `100 + 20 + 251 = 371` |

**C# binding** (`NativeMethods` in `src/StockPilot/Native/NativeMethods.cs`):
`[DllImport("stockpilot_native", EntryPoint = "calc_handling_fee", CallingConvention = CallingConvention.Cdecl)] static extern int CalcHandlingFee(int, int)`.
`DllImport` needs no project setting (`AllowUnsafeBlocks` is only required by `LibraryImport`). On Windows the runtime looks for `stockpilot_native.dll`, on Linux `libstockpilot_native.so`.

**Build:** `native/build.ps1` runs `gcc -shared -O2 -Wall -Wextra -Inative/include -o native/runtime/win-x64/stockpilot_native.dll native/src/fees.c`
(or `cl /LD /O2 ...` when only MSVC is found). Target: Windows x64.
**Where the binary is found:** the build writes it to `native/runtime/win-x64/`. `StockPilot.csproj` (and the test project) copy everything from `native/runtime/<rid>/` next to the executable on every build, so after a clean build the DLL sits in `bin/Debug/net8.0/` beside `StockPilot.dll`.

**Adapter** (`NativeFeeCalculator : IFeeCalculator`): validates the domain input first (`InvalidFeeInput`, native function not called), calls C, and turns `DllNotFoundException`, `EntryPointNotFoundException`, `BadImageFormatException` into `Failure(NativeUnavailable)`; a `-1` from C becomes `Failure(NativeRejected)`.
**Used in the real workflow:** `OrderWorkflow.Process` -> `fees.Calculate(...)` -> the fee is part of `OrderReceipt` and of every confirmed order line. `NativeFeeCalculator` is in `src/StockPilot/Native/NativeFeeCalculator.cs`.

**Integration difficulty and how it was resolved:** The computer already had an old 32-bit compiler, MinGW.org GCC 6.3.0, in `C:\MinGW\bin`. After installing WinLibs, `gcc -dumpmachine` still printed `mingw32` instead of `x86_64-w64-mingw32`, and `where.exe gcc` listed two compilers with the old one first in `PATH`. That compiler would have built a 32-bit DLL, which a 64-bit .NET process cannot load (`BadImageFormatException`). Fix: the WinLibs `mingw64\bin` folder was put at the beginning of `PATH` for the build session, `gcc -dumpmachine` was checked again (`x86_64-w64-mingw32`), `build-native.cmd` printed `Compiler: gcc (x86_64-w64-mingw32)`, and the architecture of the result was confirmed with `objdump -f` (`pei-x86-64`):

```
objdump -f native\runtime\win-x64\stockpilot_native.dll

native\runtime\win-x64\stockpilot_native.dll:     file format pei-x86-64
architecture: i386:x86-64, flags 0x0000013b:
HAS_RELOC, EXEC_P, HAS_DEBUG, HAS_SYMS, HAS_LOCALS, D_PAGED
```

The export was checked with `objdump -p native\runtime\win-x64\stockpilot_native.dll | findstr calc_handling_fee` (the function `calc_handling_fee` is in the export table).
(Typical causes: a 32-bit compiler produces a DLL that a 64-bit .NET cannot load; the script is blocked by the execution policy — `build-native.cmd` passes `-ExecutionPolicy Bypass`; the library name in `DllImport` must be written without `.dll`.)

## 7. Run, failure demonstrations
| Demonstration | Command (repository root) | Expected |
|---|---|---|
| Normal run | `dotnet run --project src/StockPilot` | all sections, exit code 0 |
| One workflow | `dotnet run --project src/StockPilot -- deliveries` | receipts above, `output/products_after.json` saved |
| Wrong command | `dotnet run --project src/StockPilot -- frobnicate` | `ERROR [InvalidArguments] ...` + usage, exit code 1 |
| Missing data file | `dotnet run --project src/StockPilot -- orders --orders data/missing.json` | `ERROR [FileMissing] ...`, exit code 1, nothing written |
| Malformed data file | `dotnet run --project src/StockPilot -- orders --orders data/malformed_orders.json` | `ERROR [MalformedData] ...`, exit code 1 |
| Native library missing | `dotnet build`, rename `src\StockPilot\bin\Debug\net8.0\stockpilot_native.dll`, then `dotnet run --no-build --project src/StockPilot -- orders` | native check `FAILED [NativeUnavailable] ...`; every order that passed validation is `REJECTED [NativeUnavailable]`; stock unchanged |
| Output cannot be written | `dotnet run --project src/StockPilot -- deliveries --out README.md` | `FAILED [IoError] ...`, exit code 1, nothing half written |

## 8. Tests (`tests/StockPilot.Tests`)
**106 test methods, 135 test cases** (xUnit). Required minimum: 12 (8 pure/domain, 2 workflow, 2 file).
| Folder / class | Covers |
|---|---|
| `Pipeline/PipelineTests` | workflow 1: normal, empty, boundary, zero values, two policies on the same data, purity, null arguments |
| `Orders/OrderTests` | factory, distinct cases, pattern matching, invalid sequence, repeated confirmation, state preservation |
| `ErrorHandling/ErrorHandlingTests` | Result/Maybe, Map/Bind (a counter proves skipped functions), three+ domain failures, missing and malformed files |
| `Reports/ReportsAndNativeTests` | both reports (empty, no match, ties, grouping), both extensions, currying, deferred execution with a probe, disposal, **real native call**, adapter failures, two complete workflows, CSV |
| `Deliveries/DeliveryTests` | delivery factory (each rule + all broken rules), inclusive capacity boundary, overflow, Result chain, zero stock, duplicate delivery, batch with accepted and rejected lines, empty batch, ledger immutability, JSON input, stock JSON round trip |
| `App/ApplicationTests` | command-line parsing, whole application via `StringWriter`: deliveries (and saved stock loaded again), orders with the real native library, demo, reports CSV, help, unknown command, missing file, malformed file, native library unavailable, output cannot be written |

The native tests call the **real** library . The load failures use real bindings to a library that does not exist and to a wrong function name.

**Latest test outcome:** total: 135, failed: 0, passed: 135, skipped: 0, duration: 8.5s

## 9. Requirement map (final exam)
| Requirement | Implemented in |
|---|---|
| ≥ 3 workflows, ≥ 2 domain models, ≥ 2 reports | `RestockPipeline`, `OrderWorkflow.Process`, `DeliveryWorkflow.Receive`; `Catalog`/`Product`, orders & deliveries; `Reports.PurchasingList`, `Reports.ValueByCategory` |
| Pure core separated from side effects | section 3; only `IO/`, `Native/NativeMethods`, `App/`, `Program` touch the outside world |
| Immutable values, caller data preserved | records, `ImmutableArray`, `Catalog` (immutable dictionary), `DeliveryLedger`; tests assert inputs unchanged |
| Pattern matching | `OrderWorkflow.Confirm`, `ConfirmReservation`, `DeliveryWorkflow.ToResult`, `Result.Match`, `MaybeExtensions` |
| Pipeline ≥ 3 named stages | `Stages.SelectRestockCandidates` → `ScoreCandidates` → `BuildPlan` |
| Higher-order function + 2 policies | `Stages.ScoreCandidates` / `RestockPipeline.Run` take a `Func<Product,long>`; `Policies.Conservative`, `Policies.ValueWeighted` |
| Validated factories, no unchecked construction | `ValidatedOrder.Create`, `ValidatedDelivery.Create` (private constructors) |
| Distinct cases with payloads | `OrderValidation`, `Reservation`, `TransitionOutcome`, `DeliveryValidation` |
| Invalid sequence rejected, no partial state | repeated/rejected confirmation (`InvalidState`), duplicate delivery (`DuplicateDelivery`); failures return no new catalog |
| Result for ≥ 3 domain failures | section 4, error table |
| Map **and** Bind in a real workflow | `OrderWorkflow.Process`, `DeliveryWorkflow.Receive`, `AppFlow.Load` |
| Missing lookups explicit | `Catalog.Find` / `Catalog.StockOf` → `Maybe` (`Something` / `Nothing`); `.ToResult(...)` when the item is required |
| File and parsing exceptions at boundaries | `DataFile.Load`, `ReportFile.Save` |
| ≥ 2 custom extension methods, used | `QueryExtensions.LowStock`, `ResultExtensions.Ensure` |
| 2 reports: filter, projection, order + tie-breaker, GroupBy, aggregate | `Reports.PurchasingList`, `Reports.ValueByCategory` |
| Curried function, 2 partial applications | `Scoring.CreateScore`, `Scoring.Urgent`, `Scoring.Economy` |
| `yield return`, deferred execution, materialisation | `OrderWorkflow.ProcessLazily`, `OrderWorkflow.ProcessAll` |
| Load JSON, save updated data / export | `DataFile.Load`; `ReportFile.Save` (CSV reports, `products_after.json`) |
| Resource handling | `ReportFile.Save` (`using`), `DataFile.Load` (convenience API), section 5 |
| Native C library, P/Invoke, adapter, real call used in a workflow | `native/src/fees.c`, `NativeMethods`, `NativeFeeCalculator`, `OrderWorkflow.Process` |

## 10. Limitations
- The delivery ledger lives in memory for one run: a delivery reference is detected as a repeat **within a run**, not across runs. (The saved `products_after.json` holds stock only.)
- The prebuilt native binary is for Windows x64 only; other platforms rebuild it with `native/build.sh` .
- Money is integer minor units; there are no currencies and no tax.
- Console application only; no web or desktop interface.
