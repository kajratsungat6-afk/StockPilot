#include "fees.h"

#define MAX_TOTAL_MINOR 1000000000
#define MAX_QUANTITY    10000

int32_t calc_handling_fee(int32_t order_total_minor, int32_t quantity)
{
    if (order_total_minor < 0 || order_total_minor > MAX_TOTAL_MINOR) return -1;
    if (quantity < 1 || quantity > MAX_QUANTITY) return -1;

    /* 2.5 % = 25 / 1000. Rounded half up with integers; int64_t avoids overflow (1e9 * 25 > int32). */
    int64_t percent = ((int64_t)order_total_minor * 25 + 500) / 1000;

    return (int32_t)(100 + 20 * (int64_t)quantity + percent);
}
