import { useFormContext } from "react-hook-form";
import { useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardLicenseKeyStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { licenseKeyStep } = useWatch({ control });

    return <h1>License key</h1>;
}
