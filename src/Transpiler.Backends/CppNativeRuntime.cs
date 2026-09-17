namespace Transpiler.Backends;

/// <summary>Original C++20 scalar helpers. Arithmetic never relies on signed-overflow undefined behavior.</summary>
internal static class CppNativeRuntime
{
    public const string Source = """
        template<class T> using unsigned_t = std::make_unsigned_t<T>;
        template<class T> inline unsigned_t<T> uns(T value) { return std::bit_cast<unsigned_t<T>>(value); }
        template<class T> inline T bits(unsigned_t<T> value) { return std::bit_cast<T>(value); }
        [[noreturn]] inline void overflow() { throw std::overflow_error("System.OverflowException"); }
        [[noreturn]] inline void divide_zero() { throw std::domain_error("System.DivideByZeroException"); }
        template<class T> inline T add(T a, T b) { return bits<T>(uns(a) + uns(b)); }
        template<class T> inline T sub(T a, T b) { return bits<T>(uns(a) - uns(b)); }
        template<class T> inline T mul(T a, T b) { return bits<T>(uns(a) * uns(b)); }
        template<class T> inline T neg(T a) { return bits<T>(unsigned_t<T>{0} - uns(a)); }
        template<class T> inline T bit_not(T a) { return bits<T>(~uns(a)); }
        template<class T> inline T bit_and(T a, T b) { return bits<T>(uns(a) & uns(b)); }
        template<class T> inline T bit_or(T a, T b) { return bits<T>(uns(a) | uns(b)); }
        template<class T> inline T bit_xor(T a, T b) { return bits<T>(uns(a) ^ uns(b)); }
        template<class T, class S> inline T shl(T a, S b) {
            const auto n = static_cast<unsigned>(b) & (sizeof(T) * 8 - 1);
            return bits<T>(uns(a) << n);
        }
        template<class T, class S> inline T shr_u(T a, S b) {
            const auto n = static_cast<unsigned>(b) & (sizeof(T) * 8 - 1);
            return bits<T>(uns(a) >> n);
        }
        template<class T, class S> inline T shr(T a, S b) {
            const auto n = static_cast<unsigned>(b) & (sizeof(T) * 8 - 1);
            auto result = uns(a) >> n;
            if (a < 0 && n != 0) result |= ~unsigned_t<T>{0} << (sizeof(T) * 8 - n);
            return bits<T>(result);
        }
        template<class T, bool Unsigned> inline T add_checked(T a, T b) {
            if constexpr (Unsigned) {
                if (uns(a) > std::numeric_limits<unsigned_t<T>>::max() - uns(b)) overflow();
            } else {
                if ((b > 0 && a > std::numeric_limits<T>::max() - b) ||
                    (b < 0 && a < std::numeric_limits<T>::min() - b)) overflow();
            }
            return add(a, b);
        }
        template<class T, bool Unsigned> inline T sub_checked(T a, T b) {
            if constexpr (Unsigned) {
                if (uns(a) < uns(b)) overflow();
            } else {
                if ((b < 0 && a > std::numeric_limits<T>::max() + b) ||
                    (b > 0 && a < std::numeric_limits<T>::min() + b)) overflow();
            }
            return sub(a, b);
        }
        template<class T, bool Unsigned> inline T mul_checked(T a, T b) {
            if constexpr (Unsigned) {
                if (uns(b) != 0 && uns(a) > std::numeric_limits<unsigned_t<T>>::max() / uns(b)) overflow();
            } else {
                const auto min = std::numeric_limits<T>::min(), max = std::numeric_limits<T>::max();
                if (a > 0) { if ((b > 0 && a > max / b) || (b < 0 && b < min / a)) overflow(); }
                else if (a < 0) { if ((b > 0 && a < min / b) || (b < 0 && a < max / b)) overflow(); }
            }
            return mul(a, b);
        }
        template<class T, bool Unsigned, bool Remainder> inline T divide(T a, T b) {
            if (b == 0) divide_zero();
            if constexpr (Unsigned) {
                if constexpr (Remainder) return bits<T>(uns(a) % uns(b));
                else return bits<T>(uns(a) / uns(b));
            } else {
                // Deliberate portable profile policy, also matching the x64 CoreCLR oracle.
                if (a == std::numeric_limits<T>::min() && b == T{-1}) overflow();
                if constexpr (Remainder) return a % b;
                else return a / b;
            }
        }
        template<class D, bool Checked, bool UnsignedSource, class T>
        inline auto convert(T a) -> std::conditional_t<(sizeof(D) < 8), std::int32_t, std::int64_t> {
            if constexpr (Checked) {
                if constexpr (UnsignedSource) {
                    if (static_cast<std::uint64_t>(uns(a)) > static_cast<std::uint64_t>(std::numeric_limits<D>::max())) overflow();
                } else if constexpr (std::is_signed_v<D>) {
                    const auto value = static_cast<std::int64_t>(a);
                    if (value < static_cast<std::int64_t>(std::numeric_limits<D>::min()) ||
                        value > static_cast<std::int64_t>(std::numeric_limits<D>::max())) overflow();
                } else {
                    if (a < 0 || static_cast<std::uint64_t>(a) > static_cast<std::uint64_t>(std::numeric_limits<D>::max())) overflow();
                }
            }
            using U = std::make_unsigned_t<D>;
            U raw;
            if constexpr (sizeof(D) > sizeof(T) && (std::is_unsigned_v<D> || UnsignedSource)) raw = static_cast<U>(uns(a));
            else raw = static_cast<U>(a);
            if constexpr (std::is_unsigned_v<D> && sizeof(D) < 4) return static_cast<std::int32_t>(raw);
            else if constexpr (sizeof(D) < 4) return static_cast<std::int32_t>(std::bit_cast<std::make_signed_t<D>>(raw));
            else return std::bit_cast<std::make_signed_t<D>>(raw);
        }
        """;
}
