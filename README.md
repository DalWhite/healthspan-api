# healthspan-api

````
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Passw0rd" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:latest 
docker run -e "MYSQL_ROOT_PASSWORD=Passw0rd" -p 3306:3306 --name mysql -d mysql:latest
````

TODOS:
- [ ] Return json with id on posts and puts
- [ ] Allow to add workout relation on post exercises
- https://barbend.com/how-to-use-rpe-scale-strength-training/