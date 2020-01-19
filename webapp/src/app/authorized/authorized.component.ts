import {Component, OnInit} from '@angular/core';
import {NewReceiptComponent} from "../new-receipt/new-receipt.component";
import {MatDialog} from "@angular/material/dialog";
import {AddReceiptBatchDialogComponent} from "../add-receipt-batch-dialog/add-receipt-batch-dialog.component";

@Component({
  selector: 'app-authorized',
  templateUrl: './authorized.component.html',
  styleUrls: ['./authorized.component.scss']
})
export class AuthorizedComponent implements OnInit {

  constructor(private dialog: MatDialog) {
  }

  ngOnInit() {
  }

  openNewReceiptDialog(): void {
    this.dialog.open(NewReceiptComponent, {
      width: '700px'
    });
  }

  openNewBatchReceiptDialog(): void {
    this.dialog.open(AddReceiptBatchDialogComponent, {
      width: '700px'
    });
  }
}
