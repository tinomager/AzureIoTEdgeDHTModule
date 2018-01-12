# AzureIoTEdgeDHTModule
Azure IoT Edge module to serve DHT sensor telemetry to the runtime.
Module is written in C# and contains a Dockerfile for running on Raspberry Pi 3
This module depends on running the DHT server for abstraction of the hardware sensor - the DHT server can be found here: https://github.com/tinomager/DHTServer

The module supports two desired properties that can be configured via Azure portal.
1. "interval" -> defines the sleep interval between each run of querying the DHT sensor and sending the data to Azure IoT Edge runtime.
2. "localhosturl" -> defines the url / ip of the running DHT server. DHT server can run on the hostmachine of Azure IoT Edge runtime. Sample configuration "http://{ip of docker host}:{port}

The module supports one output named "output1".
