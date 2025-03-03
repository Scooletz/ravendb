import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardUsePackageStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { usePackageStep } = useWatch({ control });

    return <h1>Use setup package</h1>;
}
