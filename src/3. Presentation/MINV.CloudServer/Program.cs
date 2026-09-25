using MINV.CloudServer;

// V4 · Servidor en la nube del escritorio M-INV (ver CloudServerApp).
var app = CloudServerApp.Build(args);
await CloudServerApp.RunAsync(app);
