using System.Text;
using ProjectManager.CLI;

Console.OutputEncoding = Encoding.UTF8;

return await CliApplication.RunAsync(args, Console.Out, Console.Error);
