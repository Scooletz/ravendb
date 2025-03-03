import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardSecurityStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { securityStep } = useWatch({ control });

    return <h1>Security</h1>;
}
