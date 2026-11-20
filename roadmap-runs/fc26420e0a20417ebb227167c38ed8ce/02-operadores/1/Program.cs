using System;
using System.Globalization;

if (args.Length != 1)
{
    Console.Error.WriteLine("Esperado um unico argumento no formato operacao:argumentos.");
    return 1;
}

string comando = args[0];
int separador = comando.IndexOf(':');

if (separador < 0 ||
    !int.TryParse(comando[(separador + 1)..],
        NumberStyles.AllowLeadingSign,
        CultureInfo.InvariantCulture,
        out int numero))
{
    Console.Error.WriteLine("Comando invalido. Use identidade:<int> ou dobro:<int>.");
    return 1;
}

string operacao = comando[..separador];

try
{
    int resultado = operacao switch
    {
        "identidade" => numero,
        "dobro" => checked(numero * 2),
        _ => throw new ArgumentException("Operacao desconhecida.")
    };

    Console.WriteLine(resultado.ToString(CultureInfo.InvariantCulture));
    return 0;
}
catch (OverflowException)
{
    Console.Error.WriteLine("O dobro excede o intervalo de Int32.");
    return 1;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("Operacao desconhecida. Use identidade ou dobro.");
    return 1;
}
