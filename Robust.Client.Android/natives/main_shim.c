// SDLActivity loads "libmain.so" and calls SDL_main on its own thread.
// The managed side registers the real entry point before starting the activity.
#include <stddef.h>

typedef int (*robust_main_fn)(int argc, char **argv);
static volatile robust_main_fn g_main = NULL;

__attribute__((visibility("default"))) void robust_set_main(robust_main_fn fn) { g_main = fn; }

__attribute__((visibility("default"))) int SDL_main(int argc, char **argv)
{
    if (g_main == NULL)
        return 1;
    return g_main(argc, argv);
}
