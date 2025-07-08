import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");

class licenseSendVerificationCodeCommand extends commandBase {
    constructor(private licensePayload: SendFreeLicenseVerificationRequest) {
        super();
    }
    
    execute(): JQueryPromise<void> {
        const url = endpoints.global.license.licenseFreeSendVerificationCode;
        
        return this.post(url, JSON.stringify(this.licensePayload), null, { dataType: undefined })
            .fail((response: JQueryXHR) => this.reportError(response.responseText, response.responseText, response.statusText));
    }
}

export = licenseSendVerificationCodeCommand