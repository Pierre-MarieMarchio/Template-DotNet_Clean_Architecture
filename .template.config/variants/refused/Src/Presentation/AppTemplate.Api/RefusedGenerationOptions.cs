//#if (auth == "none" && examples != "none")
#error The template refuses --auth none with example features, which each belong to a caller: generate with --examples none.
//#endif
//#if (auth == "none" && files)
#error The template refuses --auth none with the Files module, whose files belong to a caller: generate with --files false.
//#endif
//#if (web != "none" && webAudience == "admin" && auth == "none")
#error The template refuses --web-audience admin with --auth none, since a back office signs administrators in: generate with --auth local or --auth external.
//#endif
//#if (database == "sqlserver" || database == "mysql" || database == "sqlite")
#error This version of the template generates --database postgresql only: generate with --database postgresql.
//#endif
#error Generate the project again without the hidden option that let the combination through.
