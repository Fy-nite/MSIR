namespace TestingApp
{
    public class Program
    {
        static void Main(string[] args)
        {
            var a = "meow";
            if (a == "meow")
            {
                a = "woof";
            }

            if (a == "meow")
            {
                a = "woof";
            }
            else
            {
                Console.WriteLine("a is not meow");
            }
            Console.WriteLine("Hello, World! " +  a );

            // Additional samples to exercise IL -> ObjectIR mappings
            Console.WriteLine("SumArray => " + SumArray());
            Console.WriteLine("SumTo(5) => " + SumTo(5));
            Console.WriteLine("CreateAndReturn(\"x\") => " + CreateAndReturn("x"));
        }

        static int SumArray()
        {
            var arr = new int[3];
            arr[0] = 1;
            arr[1] = 2;
            arr[2] = 3;
            int s = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                s += arr[i];
            }
            return s;
        }

        static int SumTo(int n)
        {
            int s = 0;
            for (int i = 0; i < n; i++) s += i;
            return s;
        }

        static string CreateAndReturn(string x)
        {
            // newobj/newarr and call patterns
            var prefix = "X:" + x;
            return prefix;
        }
    }
}
