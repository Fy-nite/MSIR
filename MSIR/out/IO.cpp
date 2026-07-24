#include <iostream>
#include <string>
#include <algorithm>
#include <memory>
#include <vector>

// Generated from module: MSIRTest

namespace MSIRTest {

class IO {
public:
    static Void Println(Object s) {
        return; // void
    }

    static std::string ReadLn(Object s) {
        std::string loc0{};
        loc0 = "";
        return loc0;
    }

    static Void Write(Object s) {
        return; // void
    }

    Void _ctor() {
        // Unhandled opcode typed: ldarg this
        return System::Object::_::ctor();
    }

};

} // namespace MSIRTest
