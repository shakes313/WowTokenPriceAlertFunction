Function App Steps:
- Create Azure Function App project. Deselect azurite and seelct 6.0 LT (not isolated)
	- Paste code into Function1.cs file (can also rename it if you like)
	- Paste json into host.json
	- Add packages:
		- Azure.Communication.Sms
		- Azure.Storage.Blobs
	- Build Solution (and fix any errors)
	- Login to Azure and create a free account
	- Right click on the function name in the Solution Explorer and click "Publish…" or on the menu: Build > Publish
		- Select Azure, Azure Function App (Windows)
		- Click "Create a new instance"
		- Plan Type: Consumption
  		- Set you location to your region (and for Storage and AI)
		- Click 'Create'. Note you should now see the resources being created in Azure. This may take a couple minutes.
	- In Azure, select "All Resources" and click on the Function App
	- Click "Stop"
	- Generate an App Password for your gmail account (https://myaccount.google.com/apppasswords)
	- Under Settings, select "Environment Variables" and create the following settings. Click add, enter the name 
        below and the appropriate value (provided example values in parenthesis):
		- TOKEN_AMOUNT_ALERT_LOW (230000)
		- TOKEN_AMOUNT_ALERT_HIGH (350000)
		- GET_TOKEN_CALL_MAX_TRIES (3)
		- GET_TOKEN_CALL_RETRY_DELAY_MS (700)
		- FROM_EMAIL (bob@gmail.com)
		- ON_ALERT_EMAIL_TO (bob@iCloud.com;doug@yahoo.com)
		- ALWAYS_NOTIFY_EMAIL_TO (bob@gmail.com)
		- FROM_EMAIL_PASSWORD
		- WCS_CONNECTION_STRING
		- WCS_PHONE_NUMBER
		- AzureFunctionsJobHost__logging__logLevel__Default (e.e. None, Information or Error)
	- You can stop all logging by changing the setting to None.
   		- Can also delete the two related resources with Types: 
			- Application Insights and Log Analytics workspace.
		- I just set my setting to Information when debugging and Error at all other times. 
		- Overall, it appears to be a difference of about 5-30 cents per month. 
            	This is due to the cost of the Storage Account (which should be the only resource incurring a charge). 
            	There will always be a cost resulting from the storage of the function (roughly 1/4 - 1/2 cent per day… 5-15 cents per month). 
            	I advise setting it to Information until verifying all is working correctly. And set the Min/Max amount to a current 
            	value so the emails are all sent. Then set these back as desired with the logging to None or Error.
	- Click "Overview" and click "Start"
	- If you see an error when running the app regarding "System.Memory.Data", add this to the proj file and re-publish:
	  <ItemGroup>
	    <FunctionsPreservedDependencies Include="System.Memory.Data.dll" />
	  </ItemGroup>
	- Under the Monitoring section, select Logs and run the "Traces" query. Keep redfreshing until you see log data come 
        in - and verify the emails are sent. 
	- If all works correctly, this should run every 5 minutes (with a 15 second delay), checking for an updated token price, 
        sending an email to ALWAYS_NOTIFY_EMAIL_TO whenever it changes, regardless of the thresholds set - and send 
        to ON_ALERT_EMAIL_TO when the price is under/over the min/max.
    - Lastly, under the portal menu, click on "Cost Analysis" to view any costs related to the added resources. At max, with 
        informational logging enabled, it should not be more than 2 cents per day. If you disable logging and/or simply remove 
        the two logging resources, it should be less than 1/2 cent per day.
