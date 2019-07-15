import {Component, OnInit} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {ReceiptService} from '../receipt.service';

@Component({
  selector: 'app-new-receipt',
  templateUrl: './new-receipt.component.html',
  styleUrls: ['./new-receipt.component.scss']
})
export class NewReceiptComponent implements OnInit {
  newReceiptForm = new FormGroup({
    barCodeText: new FormControl('')
  });

  constructor(private receiptService: ReceiptService) {
  }

  ngOnInit() {
  }

  onSubmit() {
    console.log(this.newReceiptForm.value.barCodeText);
    this.receiptService.addReceiptByBarCode(this.newReceiptForm.value.barCodeText).subscribe();
  }
}
