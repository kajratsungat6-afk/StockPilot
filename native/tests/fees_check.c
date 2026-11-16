/* Manual check of the C function without .NET: gcc -I native/include native/src/fees.c native/tests/fees_check.c -o fees_check */
#include <stdio.h>
#include "fees.h"

static int failures = 0;
static void check(int32_t total, int32_t qty, int32_t expected)
{
    int32_t actual = calc_handling_fee(total, qty);
    printf("%s calc_handling_fee(%d, %d) = %d (expected %d)\n", actual == expected ? "OK  " : "FAIL", total, qty, actual, expected);
    if (actual != expected) failures++;
}

int main(void)
{
    check(10000, 3, 410);          /* 100 + 60 + 250                          */
    check(10020, 1, 371);          /* 2.5% of 10020 = 250.5 -> rounds up to 251 */
    check(10019, 1, 370);          /* 250.475 -> 250                          */
    check(0, 1, 120);              /* lower bounds                            */
    check(3000, 2, 215);           /* 100 + 40 + 75                           */
    check(125000, 5, 3325);        /* 100 + 100 + 3125                        */
    check(16000, 40, 1300);        /* 100 + 800 + 400                         */
    check(1000000000, 10000, 25200100); /* upper bounds                       */
    check(-1, 1, -1);              /* invalid                                 */
    check(1000000001, 1, -1);
    check(100, 0, -1);
    check(100, 10001, -1);
    return failures == 0 ? 0 : 1;
}
