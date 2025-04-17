#!/bin/sh
RETVAL=0
CURRENT_HOME=$(cd `dirname $0`;pwd)

echo CURRENT_HOME: $CURRENT_HOME
run()
{
    MODE=$2
    if [ $MODE = "chroot" ];
    then
        export IS_CHROOT=1
    fi
    if [ -f $CURRENT_HOME/bin/mysqld ];
    then
        chmod +x $CURRENT_HOME/YiQiDong.MySQL
        $CURRENT_HOME/YiQiDong.MySQL
    else
        echo "File[$CURRENT_HOME/YiQiDong.MySQL] not found!"
        exit 127
    fi
}
chroot_umount()
{
    CHROOT_HOME=$2
    if [ -z $CHROOT_HOME ];
    then        
        echo "parameter [CHROOT_HOME] is missing"
        exit 127
    fi
    if [ ! -d $CHROOT_HOME ];
    then
        echo "Error: CHROOT_HOME [$CHROOT_HOME] not found."
        exit 127
    fi
    umount $CHROOT_HOME/proc
    umount $CHROOT_HOME/sys
    if [ $# -ge 3 ] ;
    then
        index="+"
        for i in "$@";
        do
            if [ ${#index} -ge 3 ] ;
            then
                if [ -d $i ];
                then
                    if [ -d $CHROOT_HOME$i ];
                    then
                        umount $CHROOT_HOME$i
                    fi
                fi
            fi
            index=$index+
        done
    fi
}
chroot_mount()
{
    CHROOT_HOME=$2
    if [ -z $CHROOT_HOME ];
    then        
        echo "parameter [CHROOT_HOME] is missing"
        exit 127
    fi
    if [ ! -d $CHROOT_HOME ];
    then
        echo "Error: CHROOT_HOME [$CHROOT_HOME] not found."
        exit 127
    fi
    mount -t proc /proc $CHROOT_HOME/proc
    mount -t sysfs /sys $CHROOT_HOME/sys
    
    if [ $# -ge 3 ] ;
    then
        index="+"
        for i in "$@";
        do
            if [ ${#index} -ge 3 ] ;
            then
                echo "Mounting [$i]..."
                if [ ! -d $i ];
                then
                    mkdir -p $i
                fi
                if [ ! -d $CHROOT_HOME$i ];
                then
                    mkdir -p $CHROOT_HOME$i
                fi
                mount -o bind $i $CHROOT_HOME$i
            fi
            index=$index+
        done
            
    fi
}
chroot_run()
{
    CHROOT_HOME=$2
    if [ -z $CHROOT_HOME ];
    then        
        echo "Error: Parameter [CHROOT_HOME] is missing"
        exit 127
    fi
    if [ ! -d $CHROOT_HOME ];
    then
        echo "Error: CHROOT_HOME [$CHROOT_HOME] not found."
        exit 127
    fi
    chroot_umount $*
    chroot_mount $*
    chroot $CHROOT_HOME $CURRENT_HOME/mysql.sh run chroot
}

case "$1" in
 run)
        run $*
        ;;
 chroot_run)
        chroot_run $*
        ;;
 chroot_mount)
        chroot_mount $*
        ;;
 chroot_umount)
        chroot_umount $*
        ;;
esac
exit $RETVAL