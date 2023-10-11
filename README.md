# healthspan-api

````
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Passw0rd" -p 1433:1433 --name sqlserver -d mcr.microsoft.com/mssql/server:latest 
````

TODOS:
- [ ] Solve creation of migrations with diferent providers (depending on the env also). Being able to switch provider quickly