const path = require('path');
const grpc = require('@grpc/grpc-js');
const protoLoader = require('@grpc/proto-loader');

const PROTO_PATH = path.join(__dirname, '..', 'proto', 'calc.proto');
const PORT = process.env.PORT || 5002;

const definition = protoLoader.loadSync(PROTO_PATH, { keepCase: true, defaults: true });
const calc = grpc.loadPackageDefinition(definition).calc;

const INT32_MAX = 2147483647;

function fail(callback, code, details) {
  callback({ code, details });
}

const handlers = {
  Sum: (call, callback) => callback(null, { result: call.request.x + call.request.y }),
  Sub: (call, callback) => callback(null, { result: call.request.x - call.request.y }),
  Mul: (call, callback) => callback(null, { result: call.request.x * call.request.y }),

  Div: (call, callback) => {
    const { x, y } = call.request;
    if (y === 0) return fail(callback, grpc.status.INVALID_ARGUMENT, 'DIV: division by zero (y = 0)');
    callback(null, { result: x / y });
  },

  Fact: (call, callback) => {
    const { x } = call.request;
    if (x < 0) return fail(callback, grpc.status.INVALID_ARGUMENT, `FACT: x must be >= 0 (x = ${x})`);
    let result = 1;
    for (let i = 2; i <= x; i++) {
      result *= i;
      if (result > INT32_MAX)
        return fail(callback, grpc.status.OUT_OF_RANGE, `FACT: ${x}! exceeds int32 range`);
    }
    callback(null, { result });
  },
};

const server = new grpc.Server();
server.addService(calc.Calculator.service, handlers);
server.bindAsync(`0.0.0.0:${PORT}`, grpc.ServerCredentials.createInsecure(), (err, port) => {
  if (err) throw err;
  console.log(`gRPCServer02 (Node.js) listening on 0.0.0.0:${port}`);
});
