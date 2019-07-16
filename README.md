# ReceiptCollector

Russian Tax service provides mobile application "Проверка чека" to get receipt information online.
ReceiptCollector uses nalog.ru api to collect purchase data.


### how to build
```bash
sudo chmod +x ./build.sh 
./build.sh
```

### how to run
```bash
sudo chmod +x ./up.sh 
./up.sh
```

### how to stop
```bash
sudo chmod +x ./down.sh 
./down.sh
```

### how to debug
run angular app
```bash
cd ./webapp
npm run start
```
and build and run third-party components 
```bash
cd ./docker/nginx
./build.sh
cd ../..
./up.dev.sh
```
