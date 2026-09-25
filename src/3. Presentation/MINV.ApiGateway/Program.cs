using MINV.ApiGateway;

// V4 · API Gateway B2B de M-INV (ver ApiGatewayApp).
var app = ApiGatewayApp.Build(args);
await ApiGatewayApp.RunAsync(app);
