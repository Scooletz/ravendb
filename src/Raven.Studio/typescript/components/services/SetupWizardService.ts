import getEulaCommand from "commands/licensing/getEulaCommand";

export default class SetupWizardService {
    async getEula() {
        return new getEulaCommand().execute();
    }
}
