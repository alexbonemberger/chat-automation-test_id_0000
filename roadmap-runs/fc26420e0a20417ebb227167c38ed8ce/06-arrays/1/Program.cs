using System;
using System.Globalization;

if (args.Length != 1)
{
    Console.Error.WriteLine("Esperado um unico argumento no formato operacao:argumentos.");
    return 1;
}

string comando = args[0];
int separador = comando.IndexOf(':');

if (separador < 0)
{
    Console.Error.WriteLine("Comando invalido. Esperado operacao:argumentos.");
    return 1;
}

string operacao = comando[..separador];
string argumentos = comando[(separador + 1)..];

if (operacao == "ordenar")
{
    string[] partes = argumentos.Split(',');
    int[] numeros = new int[partes.Length];

    for (int i = 0; i < partes.Length; i++)
    {
        if (!int.TryParse(partes[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numeros[i]))
        {
            Console.Error.WriteLine("ordenar exige inteiros separados por virgula.");
            return 1;
        }
    }

    Array.Sort(numeros);
    Console.WriteLine(string.Join(",", Array.ConvertAll(numeros, n => n.ToString(CultureInfo.InvariantCulture))));
    return 0;
}

if (!int.TryParse(argumentos, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int numero))
{
    Console.Error.WriteLine("Comando invalido. Use identidade:<int>, dobro:<int>, sinal:<int>, dia:<int>, somatorio:<int> ou ordenar:<int>,<int>...");
    return 1;
}

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
    else if (operacao == "somatorio")
    {
        if (numero < 0 || numero > 1000)
        {
            Console.Error.WriteLine("somatorio exige n entre 0 e 1000.");
            return 1;
        }

        long soma = 0;
        for (int i = 1; i <= numero; i++)
        {
            soma += i;
        }

        resultado = soma.ToString(CultureInfo.InvariantCulture);
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
    Console.Error.WriteLine("Operacao desconhecida. Use identidade, dobro, sinal, dia, somatorio ou ordenar.");
    return 1;
}
