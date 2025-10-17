import { createSlice, PayloadAction } from "@reduxjs/toolkit";
import { RootState } from "components/store";

interface SetupWizardState {
    isEulaScrolledToBottom: boolean;
}

const initialState: SetupWizardState = {
    isEulaScrolledToBottom: process.env.NODE_ENV === "development", // TODO: Remove this when we are ready to release
};

export const setupWizardSlice = createSlice({
    name: "setupWizard",
    initialState,
    reducers: {
        isEulaScrolledToBottomSet: (state, action: PayloadAction<boolean>) => {
            state.isEulaScrolledToBottom = action.payload;
        },
    },
});

export const setupWizardActions = setupWizardSlice.actions;

export const setupWizardSelectors = {
    isEulaScrolledToBottom: (state: RootState) => state.setupWizard.isEulaScrolledToBottom,
};
