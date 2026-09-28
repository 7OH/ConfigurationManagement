using System.Runtime.CompilerServices;

#if WINDOWS
using System.Windows;
#endif

// Юнит-тесты парсера rac и сборки аргументов RacClient используют internal-члены
// (RacClient.BuildArguments и SensitiveDataMasker).
[assembly: InternalsVisibleTo("ConfigurationManagement.Tests")]

#if WINDOWS
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
#endif
