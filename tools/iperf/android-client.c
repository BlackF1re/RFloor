#include <stdint.h>
#include <sys/types.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>
#include <sys/socket.h>
#include "iperf_api.h"
#include "cjson.h"
static pthread_mutex_t mutex=PTHREAD_MUTEX_INITIALIZER;
static struct iperf_test *current;
__attribute__((visibility("default"))) void rfloor_cancel(void) {
    pthread_mutex_lock(&mutex);
    if(current) {int fd=iperf_get_control_socket(current);if(fd>=0)shutdown(fd,SHUT_RDWR);}
    pthread_mutex_unlock(&mutex);
}
__attribute__((visibility("default"))) char *rfloor_run(const char *host,int port,int seconds,int reverse,const char *temporary) {
    struct iperf_test *test=iperf_new_test();if(!test)return NULL;
    iperf_defaults(test);iperf_set_test_role(test,'c');iperf_set_test_server_hostname(test,host);
    iperf_set_test_server_port(test,port);iperf_set_test_duration(test,seconds);iperf_set_test_reverse(test,reverse);
    iperf_set_test_json_output(test,1);iperf_set_test_template(test,temporary);iperf_set_test_connect_timeout(test,3000);
    pthread_mutex_lock(&mutex);current=test;pthread_mutex_unlock(&mutex);
    int status=iperf_run_client(test);char *output=iperf_get_test_json_output_string(test);char *result=NULL;
    if(status>=0&&output)result=strdup(output);
    else {cJSON *error=cJSON_CreateObject();cJSON_AddStringToObject(error,"error",iperf_strerror(i_errno));result=cJSON_PrintUnformatted(error);cJSON_Delete(error);}
    pthread_mutex_lock(&mutex);current=NULL;iperf_free_test(test);pthread_mutex_unlock(&mutex);return result;
}
__attribute__((visibility("default"))) void rfloor_free(char *value){free(value);}
