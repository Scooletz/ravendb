import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardAdditionalSettingsStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { additionalSettingsStep } = useWatch({ control });

    return <h1>Additional settings</h1>;
}
