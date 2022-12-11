namespace FodyTools.Tests.Tools
{
    using System.Linq;
    using System.Text;

    using Mono.Cecil;

    internal static class ILHelper
    {
        public static string Decompile(this MethodDefinition method)
        {
            var buf = new StringBuilder();

            buf.AppendLine(method.FullName);

            foreach (var instruction in method.Body.Instructions)
            {
                buf.Append("  ");
                buf.AppendLine(instruction.ToString());
            }

            return buf.ToString();
        }

        public static string Decompile(this TypeDefinition type)
        {
            var buf = new StringBuilder();

            buf.AppendLine(type.FullName);
            buf.AppendLine();

            foreach (var method in type.Methods.OrderBy(m => m.FullName, StringComparer.Ordinal))
            {
                buf.Append("  ");
                buf.AppendLine(method.FullName);

                foreach (var instruction in method.Body.Instructions)
                {
                    buf.Append("    ");
                    buf.AppendLine(instruction.ToString());
                }

                buf.AppendLine();
            }

            return buf.ToString();
        }
    }
}
