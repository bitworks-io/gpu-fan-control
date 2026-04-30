# Fan Control Not Working

If the utility cannot control your AMD GPU fan, try these steps:

1. Open **AMD Software: Adrenalin Edition**
2. Go to **Performance** → **Tuning**
3. In **GPU** → **Tuning Control**, enable **"Manual Tuning, Custom"**
4. Set **Fan Tuning** to **ON**
5. Set **Zero RPM** to **OFF**
6. Click **Apply Changes**

For more details, see: https://help.argusmonitor.com/GPUfancontrolforAMDRadeon.html

## Compatibility

The utility tries AMD fan-control APIs in this order:

1. ADLX for modern Radeon GPUs.
2. ADL OverdriveN for Polaris-era GPUs such as RX 470/480/570/580.
3. ADL Overdrive5 for older GPUs when the driver exposes compatible fan APIs.

Some GPUs report fan speed as RPM instead of percentage. The startup test accounts for both, but a real AMD GPU on Windows is required for validation.
