import { Injectable } from '@angular/core';
import { MatDialog } from "@angular/material/dialog";
import { Observable } from "rxjs";
import { ConfirmationDialogComponent } from "./confirmation-dialog/confirmation-dialog.component";
import { first, map } from "rxjs/operators";
import { ConfirmationDialogData } from "./confirmation-dialog/confirmation-dialog-data";

@Injectable({
  providedIn: 'root'
})
export class ConfirmationService {

  constructor(private dialog: MatDialog) {
  }

  showConfirmation(message: string): Observable<boolean> {
    const dialogData = <ConfirmationDialogData>
      {
        message: message
      };
    const matDialogRef = this.dialog
      .open(ConfirmationDialogComponent, {
        width: '500px', data: dialogData
      });
    const closed = matDialogRef.afterClosed();

    return closed.pipe(first(), map(this.mapConfirmationResult));
  }

  private mapConfirmationResult(dialogResult: string): boolean {
    return dialogResult === ConfirmationDialogComponent.DIALOG_RESULT_OK;
  }
}
