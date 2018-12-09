namespace ShellAssembly
{
    using System;

    using Microsoft.WindowsAPICodePack.Dialogs;

    using MS.WindowsAPICodePack.Internal;

    public class Program
    {
        public static void Main()
        {
            using (var dlg = new CommonOpenFileDialog { IsFolderPicker = true, InitialDirectory = ".", EnsurePathExists = true, DefaultFileName = "abc"})
            {
                if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    var x = new PropVariant();
                    var y = new PropVariant(true);

                    Console.WriteLine(x.VarType);
                    Console.WriteLine(y.VarType);

                    Console.WriteLine(JsonConvert.SerializeObject(x));
                    Console.WriteLine(JsonConvert.SerializeObject(y));

                    Console.ReadKey();
                }
            }
        }

        public static class JsonConvert
        {
            public static string SerializeObject(object value)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(value);
            }

            public static T DeserializeObject<T>(string value)
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(value);
            }

            public static void PopulateObject(string value, object target)
            {
                Newtonsoft.Json.JsonConvert.PopulateObject(value, target);
            }
        }
    }
}
