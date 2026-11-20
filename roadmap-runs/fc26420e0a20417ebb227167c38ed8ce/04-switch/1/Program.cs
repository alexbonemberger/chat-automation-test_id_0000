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
    Console.Error.WriteLine("Comando invalido. Use identidade:<int>, dobro:<int>, sinal:<int> ou dia:<int>.");
    return 1;
}

string operacao = comando[..separador];

try
{
    string resultado;

    if (operacao == "sinal")
    {
        if (numero < 0)
        {
            resultado = "NEGATIVO";
        }
        else if (numero == 0)
        {
            resultado = "ZERO";
        }
        else
        {
            resultado = "POSITIVO";
        }
    }
    else if (operacao == "dia")
    {
        resultado = numero switch
        {
            1 => "SEGUNDA",
            2 => "TERCA",
            3 => "QUARTA",
            4 => "QUINTA",
            5 => "SEXTA",
            6 => "SABADO",
            7 => "DOMINGO",
            _ => "INVALIDO"
        };
    }
    else
    {
        int resultadoNumerico = operacao switch
        {
            "identidade" => numero,
            "dobro" => checked(numero * 2),
            _ => throw new ArgumentException("Operacao desconhecida.")
        };

        resultado = resultadoNumerico.ToString(CultureInfo.InvariantCulture);
    }

    Console.WriteLine(resultado);
    return 0;
}
catch (OverflowException)
{
    Console.Error.WriteLine("O dobro excede o intervalo de Int32.");
    return 1;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("Operacao desconhecida. Use identidade, dobro, sinal ou dia.");
    return 1;
}
