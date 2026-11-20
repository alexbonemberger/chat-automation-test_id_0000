using System;
using System.Globalization;

if (args.Length != 1)
{
    Console.Error.WriteLine("Esperado um unico argumento no formato operacao:argumentos.");
    return 1;
}

const string prefixo = "identidade:";
string comando = args[0];

if (!comando.StartsWith(prefixo, StringComparison.Ordinal) ||
    !int.TryParse(comando[prefixo.Length..],
        NumberStyles.AllowLeadingSign,
        CultureInfo.InvariantCulture,
        out int numero))
{
    Console.Error.WriteLine("Comando invalido. Use identidade:<int>.");
    return 1;
}

Console.WriteLine(numero.ToString(CultureInfo.InvariantCulture));
return 0;
