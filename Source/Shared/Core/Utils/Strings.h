/*
 * Rekindled Server
 * Copyright (C) 2021 Tim Leonard
 * Copyright (C) 2026 Jake Morgeson
 *
 * This program is free software; licensed under the MIT license.
 * You should have received a copy of the license along with this program.
 * If not, see <https://opensource.org/licenses/MIT>.
 */

#pragma once

#include <string>
#include <vector>
#include <memory>

// Portable wrapper for GCC/Clang's printf-style checking attribute.
// On GNU compilers FORMAT_PRINTF(a,b) expands to
// __attribute__((format(printf,a,b))), otherwise it is empty.
//
// To make the check fire at call sites we expose a plain varargs function
// (`StringFormat`), which delegates to a va_list helper in Strings.cpp.
// Templates don't work reliably for this purpose.
//
// Parameters are the 1-based index of the format string and the first
// variadic argument.

#if defined(__GNUC__)
#  define FORMAT_PRINTF(a,b) __attribute__((format(printf,a,b)))
#else
#  define FORMAT_PRINTF(a,b)
#endif

// Public API. Definition in Strings.cpp uses va_list/vsnprintf.
std::string StringFormat(const char* format, ...) FORMAT_PRINTF(1, 2);

std::string BytesToHex(const std::vector<uint8_t>& Bytes);

// Generates a hex editor style layout.
std::string BytesToString(const std::vector<uint8_t>& Bytes, const std::string& LinePrefix);

// legacy implementation moved to cpp; see StringFormat in Strings.cpp
// for the definition using va_list.  The templated version was removed because
// it prevented the FORMAT_PRINTF attribute from being effective.

std::string TrimString(const std::string& input);

#ifdef _WIN32

//  Converts a wide utf-16 string to utf-8.
std::string NarrowString(const std::wstring& input);

//  Converts a utf-8 string to a utf-16 string.
std::wstring WidenString(const std::string& input);

#endif

//  Determines if a given string ends with another string.
bool StringEndsWith(const std::string& subject, const std::string& needle);

//  Determines if a given string starts with another string.
bool StringStartsWith(const std::string& subject, const std::string& needle);

// Returns true if all the characters in a string are human readable.
bool StringIsHumanReadable(const std::string& subject);