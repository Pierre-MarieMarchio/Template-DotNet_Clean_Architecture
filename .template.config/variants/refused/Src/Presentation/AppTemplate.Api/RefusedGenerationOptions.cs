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
//#if (web == "blazor-wasm" && auth == "none")
#error The template refuses --web blazor-wasm with --auth none, whose API publishes no route for it to call: generate with --auth local or --auth external.
//#endif
//#if (presentation != "none" && (presentationName == "Api" || presentationName == "Api.IntegrationTests" || presentationName == "Api.UnitTests" || presentationName == "Application" || presentationName == "Application.Auth" || presentationName == "Application.Auth.UnitTests" || presentationName == "Application.Core" || presentationName == "Application.Core.UnitTests" || presentationName == "Application.UnitTests" || presentationName == "Architecture.Tests" || presentationName == "Client" || presentationName == "Client.UnitTests" || presentationName == "Domain" || presentationName == "Domain.Core" || presentationName == "Domain.Core.UnitTests" || presentationName == "Domain.UnitTests" || presentationName == "EndToEnd.Tests" || presentationName == "Infrastructure.Auth" || presentationName == "Infrastructure.Auth.IntegrationTests" || presentationName == "Infrastructure.Auth.UnitTests" || presentationName == "Infrastructure.Core" || presentationName == "Infrastructure.Core.UnitTests" || presentationName == "Infrastructure.Email" || presentationName == "Infrastructure.Email.UnitTests" || presentationName == "Infrastructure.InMemory" || presentationName == "Infrastructure.InMemory.UnitTests" || presentationName == "Infrastructure.Persistence" || presentationName == "Infrastructure.Persistence.UnitTests" || presentationName == "Infrastructure.Storage" || presentationName == "Infrastructure.Storage.UnitTests" || presentationName == "Presentation.Core" || presentationName == "Presentation.Core.UnitTests"))
#error The template refuses a --presentation-name that names one of its own projects, which generation would overwrite: generate with another name, such as Portal.
//#endif
//#if (database == "sqlserver" || database == "mysql" || database == "sqlite")
#error This version of the template generates --database postgresql only: generate with --database postgresql.
//#endif
#error Generate the project again without the hidden option that let the combination through.
