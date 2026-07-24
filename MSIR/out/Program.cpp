#include <iostream>
#include <string>
#include <algorithm>
#include <memory>
#include <vector>

// Generated from module: MSIRTest

namespace MSIRTest {

class Program {
public:
    static Void Main(String__ args) {
        std::string loc0{};
        Boolean loc1{};
        loc0 = IO::Println(Hello, World!).ReadLine();
        // Unhandled opcode typed: cgt 
        loc1 = nullptr;
        if (loc1) {
        }
        return loc0.Println(loc0);
    }

    Void _ctor() {
        // Unhandled opcode typed: ldarg this
        return System::Object::_::ctor();
    }

};

} // namespace MSIRTest
int main() {
    MSIRTest::Program::Main();
    return 0;
}
