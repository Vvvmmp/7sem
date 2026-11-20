# Лабораторная работа №2 — gRPC-сервис

## Что есть в проекте

| Папка | Что это | Задание |
|---|---|---|
| `proto/calc.proto` | общий gRPC-контракт (сервис `calc.Calculator`) | — |
| `gRPCServer01/` | сервер на ASP.NET Core, порт **5001** | 1 |
| `gRPCClient01/` | консольный клиент на C# | 1, 3 |
| `gRPCServer02/` | сервер на Node.js (`@grpc/grpc-js`), порт **5002** | 2 |
| `gRPCClient02/` | клиент на Go (код из `calc.proto` уже сгенерирован в `calc/`) | 2, 3 |

## Удалённые процедуры

| RPC | Результат | Ошибка |
|---|---|---|
| `Sum(x, y)` | `x + y` | — |
| `Sub(x, y)` | `x - y` | — |
| `Mul(x, y)` | `x * y` | — |
| `Div(x, y)` | `x / y` | `y = 0` → `INVALID_ARGUMENT` |
| `Fact(x)` | `x!` (int32) | `x! > 2147483647` (т.е. `x ≥ 13`) → `OUT_OF_RANGE`; `x < 0` → `INVALID_ARGUMENT` |

Сервер сообщает об ошибке gRPC-статусом, а клиенты его перехватывают (`RpcException` в C#, `status.FromError` в Go) и печатают код и текст ошибки.

Оба сервера работают по HTTP/2 без TLS (h2c), поэтому любой клиент может подключиться к любому серверу.

## Запуск

```bash
# серверы (каждый в своём терминале)
cd gRPCServer01 && dotnet run              # :5001
cd gRPCServer02 && npm install && npm start  # :5002

# Задания 1 и 2 — каждый клиент со «своим» сервером
cd gRPCClient01 && dotnet run              # -> http://localhost:5001
cd gRPCClient02 && go run .                # -> localhost:5002

# Задание 3 — перекрёстно
cd gRPCClient02 && go run . -addr localhost:5001        # Go-клиент -> C#-сервер
cd gRPCClient01 && dotnet run -- http://localhost:5002  # C#-клиент -> Node-сервер
```

Пример вывода (одинаковый для всех четырёх комбинаций):

```
SUM(7.5, 2.5)    = 10
SUB(7.5, 2.5)    = 5
MUL(7.5, 2.5)    = 18.75
DIV(7.5, 2.5)    = 3
DIV(7.5, 0)      -> ERROR InvalidArgument: DIV: division by zero (y = 0)
FACT(5)          = 120
FACT(12)         = 479001600
FACT(13)         -> ERROR OutOfRange: FACT: 13! exceeds int32 range
FACT(-1)         -> ERROR InvalidArgument: FACT: x must be >= 0 (x = -1)
```

Перегенерировать Go-код после изменения `calc.proto` (нужны `protoc`, `protoc-gen-go`, `protoc-gen-go-grpc`):

```bash
cd gRPCClient02
protoc -I ../proto --go_out=calc --go_opt=paths=source_relative \
       --go-grpc_out=calc --go-grpc_opt=paths=source_relative ../proto/calc.proto
```
