import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardSetupMethodStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { setupMethodStep } = useWatch({ control });

    return <h1>Setup method</h1>;
}
