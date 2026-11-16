#ifndef STOCKPILOT_FEES_H
#define STOCKPILOT_FEES_H

#include <stdint.h>

#ifdef _WIN32
#  define SP_EXPORT __declspec(dllexport)
#else
#  define SP_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Handling fee for an order, in minor currency units (1500 = 15.00).
 *
 *   fee = 100 + 20 * quantity + round_half_up(order_total_minor * 2.5 %)
 *
 * Valid range : 0 <= order_total_minor <= 1 000 000 000, 1 <= quantity <= 10 000
 * Invalid input: returns -1 (a valid fee is never negative)
 * Maximum fee : 100 + 200 000 + 25 000 000 = 25 200 100 (fits in int32_t)
 * Pure and deterministic: no allocation, no global state.
 */
SP_EXPORT int32_t calc_handling_fee(int32_t order_total_minor, int32_t quantity);

#ifdef __cplusplus
}
#endif

#endif
