#define _POSIX_C_SOURCE 200809L

#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char **argv)
{
    if (argc != 2) {
        fprintf(stderr, "usage: initctl <reboot|poweroff>\n");
        return 2;
    }

    if (strcmp(argv[1], "reboot") == 0) {
        return kill(1, SIGUSR1) == 0 ? 0 : 1;
    }

    if (strcmp(argv[1], "poweroff") == 0) {
        return kill(1, SIGUSR2) == 0 ? 0 : 1;
    }

    fprintf(stderr, "initctl: unknown command: %s\n", argv[1]);
    return 2;
}
