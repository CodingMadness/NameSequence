// See https://aka.ms/new-console-template for more information

using System.Drawing;
using DotNext;

namespace NameSequence;

public class TestClass
{
    public int Value;
    public double Price;
    public string Name { get; private set; }
    public char X;

    public static TestClass[] DefineRndArray(int? count)
    {
        var allColors = Enum.GetNames<KnownColor>();

        bool takeColorsLen = count.HasValue && count.Value < allColors.Length;
        TestClass[] rnd = new TestClass[(int)(!takeColorsLen ? count : allColors.Length)!];

        for (int i = 0; i < rnd.Length; i++)
        {
            rnd[i] = new()
            {
                Price = Random.Shared.NextDouble(),
                Name = takeColorsLen ? allColors[i] : Random.Shared.NextString("abcdefghihjklmnopqertuv123456789", 25),
                Value = 100 + i,  
                X = 'A',
            };
            // rnd[i].Name.AsSpan().AsSpan()[^1] = '_';
        }
        return rnd;
    }

    public static TestClass[] DefineRndArray2()
    {
        var colorNames = new[]  { "Red" , 
            "Green", "Blue", 
            "Yellow", "White", 
            "Cyan", "Orange",
            "Magenta", "Beige"};// Enum.GetNames<KnownColor>().AsSpan(48);

        TestClass[] members = new TestClass[colorNames.Length];

        for (int i = 0; i < members.Length; i++)
        {
            members[i] = new()
            {
                Price = 102.90d,
                Value = 2001,
                Name = colorNames[i],
                X = 'O'
                //Name = colorNames[i],
           };
        }

        return members;
    }
}


//bla.Fill('X');