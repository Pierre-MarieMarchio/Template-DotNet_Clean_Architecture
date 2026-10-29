//#if (auth == "none" && examples != "none")
#error The template refuses --auth none with example features, which each belong to a caller: generate with --examples none.
//#endif
//#if (auth == "none" && files)
#error The template refuses --auth none with the Files module, whose files belong to a caller: generate with --files false.
//#endif
//#if (web != "none" && webAudience == "admin" && auth == "none")
#error The template refuses --web-audience admin with --auth none, since a back office signs administrators in: generate with --auth local or --auth external.
//#endif
//#if (presentation == "avalonia" && auth == "none")
#error The template refuses --presentation avalonia with --auth none, whose API publishes no route for it to call: generate with --auth local or --auth external.
//#endif
//#if (presentation == "wpf" && auth == "none")
#error The template refuses --presentation wpf with --auth none, whose API publishes no route for it to call: generate with --auth local or --auth external.
//#endif
//#if (web == "blazor-wasm" && auth == "none")
#error The template refuses --web blazor-wasm with --auth none, whose API publishes no route for it to call: generate with --auth local or --auth external.
//#endif
//#if (presentation == "avalonia" && presentation == "wpf")
#error The template refuses --presentation avalonia with --presentation wpf, which both write <Project>.Desktop: generate with one of them.
//#endif
//#if (jobsWorker && database == "sqlite")
#error The template refuses the worker with --database sqlite, whose file serves one process: a project without the API runs its jobs in the worker, so generate with --database postgresql or sqlserver, or with --presentation api.
//#endif
//#if (database == "sqlserver" || database == "mysql" || database == "sqlite")
#error This version of the template generates --database postgresql only: generate with --database postgresql.
//#endif
#error Generate the project again without the hidden option that let the combination through.
