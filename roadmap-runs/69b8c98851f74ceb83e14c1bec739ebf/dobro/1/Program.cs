using System;
using System.Globalization;

if (args.Length < 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int numero))
{
    Console.Error.WriteLine("Informe um inteiro em args[0].");
    return 1;
}

try
{
    Console.WriteLine(checked(numero * 2));
    return 0;
}
catch (OverflowException)
{
    Console.Error.WriteLine("O dobro excede o intervalo de Int32.");
    return 1;
}
