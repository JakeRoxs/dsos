/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#include "Shared/Core/Utils/Strings.h"
#include "Shared/Core/Utils/Logging.h"  // for Error()/Warning() macros

#include <cstdarg>
#include <sstream>
#include <iomanip>
#include <string>
#include <cassert>

#ifdef _WIN32
#include <windows.h> // WideCharToMultiByte, CP_UTF8
#endif

// public helper defined in the header; attr macro ensures compile-time
// checks on GNU compilers.
std::string StringFormat(const char* format, ...)
{
    va_list args;
    va_start(args, format);

    // vsnprintf consumes the va_list, so we either need to copy it or restart
    // it before the second call.  Use va_copy for portability and simplicity.
    va_list args_copy;
    va_copy(args_copy, args);

    // determine required buffer length; vsnprintf returns a signed int and
    // negative indicates an encoding/format error.  guard against that to
    // avoid huge allocations when casting to size_t.
    int needed = vsnprintf(nullptr, 0, format, args_copy);
    va_end(args_copy);
    if (needed < 0)
    {
        // formatting failed; log and return empty string rather than crashing
        Error("StringFormat failed in vsnprintf length query (return=%d) format=[%s]", needed, format);
        va_end(args);
        return std::string();
    }

    size_t size = static_cast<size_t>(needed) + 1;
    std::unique_ptr<char[]> buffer(new char[size]);
    int written = vsnprintf(buffer.get(), size, format, args);
    if (written < 0)
    {
        // should be rare since we already sized buffer based on first call.
        Error("StringFormat failed in vsnprintf write (return=%d) format=[%s]", written, format);
        va_end(args);
        return std::string();
    }

    va_end(args);
    return std::string(buffer.get(), buffer.get() + size - 1);
}


std::string BytesToHex(const std::vector<uint8_t>& Bytes)
{
    std::stringstream ss;
    for (uint8_t Value : Bytes)
    {
        ss << std::uppercase << std::setfill('0') << std::setw(2) << std::hex << (int)Value;
        //ss << " ";
    }

    return ss.str();
}

bool IsCharRenderable(char c)
{
    return c >= 32 && c <= 126;
}

std::string BytesToString(const std::vector<uint8_t>& Bytes, const std::string& LinePrefix)
{
    static int column_width = 32;

    std::string result = "";

    for (size_t i = 0; i < Bytes.size(); i += column_width)
    {
        std::string hex = "";
        std::string chars = "";

        for (size_t r = i; r < i + column_width && r < Bytes.size(); r++)
        {
            uint8_t Byte = Bytes[r];

            hex += StringFormat("%02X ", Byte);
            chars += IsCharRenderable((char)Byte) ? (char)Byte : '.';
        }

        result += StringFormat("%s%-97s \xB3 %s\n", LinePrefix.c_str(), hex.c_str(), chars.c_str());
    }

    return result;
}


std::string TrimString(const std::string& input)
{
    size_t startWhiteCount = 0;
    size_t endWhiteCount = 0;

    for (size_t i = 0; i < input.size(); i++)
    {
        if (input[i] < 32)
        {
            startWhiteCount++;
        }
        else
        {
            break;
        }
    }

    for (size_t i = 0; i < input.size(); i++)
    {
        if (input[input.size() - (i + 1)] < 32)
        {
            endWhiteCount++;
        }
        else
        {
            break;
        }
    }

    if (startWhiteCount + endWhiteCount >= input.size())
    {
        return "";
    }

    return input.substr(startWhiteCount, input.size() - startWhiteCount - endWhiteCount);
}

#ifdef _WIN32

std::string NarrowString(const std::wstring& input)
{
    if (input.empty())
    {
        return "";
    }

    std::string result;

    int result_length = WideCharToMultiByte(CP_UTF8, 0, input.data(), static_cast<int>(input.length()), nullptr, 0, nullptr, nullptr);
    if (result_length <= 0)
    {
        Error("NarrowString failed converting wide string; WideCharToMultiByte returned %d", result_length);
        return "";
    }

    result.resize(result_length);
    int written = WideCharToMultiByte(CP_UTF8, 0, input.data(), static_cast<int>(input.length()), result.data(), static_cast<int>(result.length()), nullptr, nullptr);
    if (written != result_length)
    {
        Error("NarrowString write mismatch: expected %d bytes but got %d", result_length, written);
    }

    return result;
}

std::wstring WidenString(const std::string& input)
{
    if (input.empty())
    {
        return L"";
    }

    std::wstring result;
    result.resize(input.size());

    int result_length = MultiByteToWideChar(CP_UTF8, 0, input.data(), static_cast<int>(input.length()), result.data(), static_cast<int>(result.length()));
    if (result_length <= 0)
    {
        Error("WidenString failed converting narrow string; MultiByteToWideChar returned %d", result_length);
        return L"";
    }

    result.resize(result_length);

    return result;
}

#endif

bool StringEndsWith(const std::string& subject, const std::string& needle)
{
    if (subject.size() >= needle.size())
    {
        size_t start_offset = subject.size() - needle.size();
        for (size_t i = start_offset; i < start_offset + needle.size(); i++)
        {
            if (subject[i] != needle[i - start_offset])
            {
                return false;
            }
        }
        return true;
    }
    return false;
}

bool StringStartsWith(const std::string& subject, const std::string& needle)
{
    if (subject.size() >= needle.size())
    {
        for (size_t i = 0; i < needle.size(); i++)
        {
            if (subject[i] != needle[i])
            {
                return false;
            }
        }
        return true;
    }
    return false;
}

bool StringIsHumanReadable(const std::string& subject)
{
    for (size_t i = 0; i < subject.size(); i++)
    {
        char c = subject[i];
        bool readable = (c >= 0x20 && c <= 0x7E);
        if (!readable)
        {
            return false;
        }
    }
    return true;
}