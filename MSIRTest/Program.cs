

using MSIRTest;

public class Program
    {
        static void Main(string[] args)
        {
            IO.Println("Hello, World!");
            var cmd = IO.ReadLn("$>");
            if (cmd != null)
            {
                IO.Println(cmd);
            }

        }
    }
